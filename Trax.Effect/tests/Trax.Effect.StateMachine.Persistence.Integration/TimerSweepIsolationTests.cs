using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The due-timer sweep fires each row in a transaction of its own. A row whose fire throws rolls back alone, is
/// counted, backs off and is cleared after a bounded number of attempts; a row with an effect pending on it is passed
/// over without holding back the rows behind it; and neither keeps the loop waking at its shortest pace. A timer's
/// fire and an effect's run are exclusive on the row: never both.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class TimerSweepIsolationTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "sweep-user";

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private InstanceHost _host = null!;
    private ThrowingLauncher _launcher = null!;
    private ControlledCharge _charge = null!;

    [SetUp]
    public void SetUp()
    {
        _launcher = new ThrowingLauncher();
        _charge = new ControlledCharge();
        _host = InstanceHost.Create(
            provider,
            launcher: _launcher,
            services: services => services.AddSingleton<IOrderCharge>(_charge)
        );
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private InvokeOutcomeReconciler Reconciler =>
        _host.Services.GetRequiredService<InvokeOutcomeReconciler>();

    private async Task MakeDue(Guid id, TimeSpan ago) =>
        (await RawSql.SetDueAt(_host, id, (await RawSql.Now(_host)) - ago)).Should().Be(1);

    // A system instance in the timed Waiting, whose timer enters Building and queues a run the launcher refuses.
    private async Task<Guid> Queueing()
    {
        var key = MachineKey.Of("sweep", Guid.NewGuid().ToString("N"));
        var instance = await _host.Start<QueueingTimerMachine>(key);
        using var scope = _host.Scope();
        (
            await scope
                .ServiceProvider.GetRequiredService<IMachineInstances>()
                .Advance<QueueingTimerMachine>(key, nameof(QueueingTimerTrigger.Open))
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        return instance.Id;
    }

    // A system reminder in its timed Review, whose timer moves it to Expired.
    private async Task<Guid> Reminder()
    {
        var key = MachineKey.Of("sweep", Guid.NewGuid().ToString("N"));
        var instance = await _host.Start<SystemReminderMachine>(key);
        using var scope = _host.Scope();
        (
            await scope
                .ServiceProvider.GetRequiredService<IMachineInstances>()
                .Advance<SystemReminderMachine>(key, nameof(ReminderTrigger.Open))
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        return instance.Id;
    }

    private async Task<Guid> ChargeInReview()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, TimedChargeMachine.Json("Review"));
        return id;
    }

    private IEnumerable<string> EffectKeys(Guid id) =>
        (
            (IMachineInternals)
                _host.Services.GetServices<IMachine>().Single(m => m.Name == TimedChargeMachine.Id)
        ).EffectKeysOf(new DraftOwner(SnapshotOwnerKind.User, User), id);

    [Test]
    public async Task A_fire_that_throws_does_not_stop_the_other_due_timers()
    {
        var poison = await Queueing();
        var healthy = await Reminder();
        await MakeDue(poison, TimeSpan.FromMinutes(2));
        await MakeDue(healthy, TimeSpan.FromMinutes(1));
        var poisonDue = (await RawSql.Timer(_host, poison))!.DueAt;

        var results = await Reconciler.FireDueTimersOnce(CancellationToken.None);

        results
            .Should()
            .ContainSingle(r => r.InstanceId == healthy)
            .Which.Outcome.Should()
            .Be(
                TimerFireResult.Fired,
                $"a fire that throws rolls back its own row, never the sweep. See {Adr}"
            );
        results
            .Should()
            .ContainSingle(r => r.InstanceId == poison)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Retrying);
        (await RawSql.Timer(_host, healthy))!.State.Should().Be(nameof(ReminderState.Expired));
        var stuck = (await RawSql.Timer(_host, poison))!;
        stuck.State.Should().Be(nameof(QueueingTimerState.Waiting), "its fire rolled back");
        stuck.DueAt.Should().Be(poisonDue, "it is tried again");
    }

    [Test]
    public async Task A_fire_that_keeps_throwing_backs_off_and_is_cleared_after_its_last_attempt()
    {
        var time = new SteppingTime();
        Reconciler.Time = time;
        Reconciler.TimerFireAttempts = 3;
        var poison = await Queueing();
        await MakeDue(poison, TimeSpan.FromMinutes(1));
        using var errors = new Counted(StateMachineInstruments.TimerFireErrors);
        using var failed = new Counted(StateMachineInstruments.TimerFireFailed);

        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == poison)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Retrying);
        errors.Count.Should().Be(1, $"each throw is counted. See {Adr}");

        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .NotContain(r => r.InstanceId == poison, "the row backs off before it is tried again");
        (await Reconciler.UntilNextTimer(CancellationToken.None))
            .Should()
            .BeGreaterThan(
                TimeSpan.FromSeconds(5),
                "a row backing off does not pace the loop at its shortest wake"
            );

        time.Advance(InvokeOutcomeReconciler.TimerErrorBackoff + TimeSpan.FromSeconds(1));
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == poison)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Retrying);
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .NotContain(r => r.InstanceId == poison, "the back-off doubles");

        time.Advance(2 * InvokeOutcomeReconciler.TimerErrorBackoff + TimeSpan.FromSeconds(1));
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == poison)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Failed, "the last attempt clears the timer");

        _launcher.Throws.Should().Be(3);
        errors.Count.Should().Be(3);
        failed.Count.Should().Be(1, "the cleared timer is counted once as failed");
        var row = (await RawSql.Timer(_host, poison))!;
        row.State.Should().Be(nameof(QueueingTimerState.Waiting));
        row.DueAt.Should()
            .BeNull($"a fire that keeps throwing is cleared, not retried forever. See {Adr}");

        time.Advance(InvokeOutcomeReconciler.NotHereBackoffCap);
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .NotContain(r => r.InstanceId == poison);
    }

    [TestCase("provider error")]
    [TestCase("timeout")]
    public async Task A_database_that_keeps_failing_never_clears_a_timer(string failure)
    {
        var time = new SteppingTime();
        Reconciler.Time = time;
        Reconciler.TimerFireAttempts = 2;
        _launcher.Failure =
            failure == "timeout"
                ? () => new InvalidOperationException("The enqueue failed.", new TimeoutException())
                : () => new DroppedConnectionException();
        var row = await Queueing();
        await MakeDue(row, TimeSpan.FromMinutes(1));
        var due = (await RawSql.Timer(_host, row))!.DueAt;

        var wait = InvokeOutcomeReconciler.TimerErrorBackoff;
        for (var i = 0; i < 4; i++)
        {
            (await Reconciler.FireDueTimersOnce(CancellationToken.None))
                .Should()
                .ContainSingle(r => r.InstanceId == row)
                .Which.Outcome.Should()
                .Be(
                    TimerFireResult.Retrying,
                    $"a failure of the database is not an attempt at the timer. See {Adr}"
                );
            (await Reconciler.FireDueTimersOnce(CancellationToken.None))
                .Should()
                .NotContain(r => r.InstanceId == row, "it is still backed off");
            time.Advance(wait + TimeSpan.FromSeconds(1));
            wait *= 2;
        }
        (await RawSql.Timer(_host, row))!.DueAt.Should().Be(due, "the timer is still armed");

        // A failure of the row itself still counts, and clears the timer after its attempts.
        _launcher.Failure = () =>
            new InvalidOperationException("The run's input could not be stored.");
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == row)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Retrying);
        time.Advance(InvokeOutcomeReconciler.TimerErrorBackoff + TimeSpan.FromSeconds(1));
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == row)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Failed);
        (await RawSql.Timer(_host, row))!.DueAt.Should().BeNull();
    }

    [Test]
    public async Task Rows_with_a_pending_effect_do_not_starve_the_rows_behind_them()
    {
        Reconciler.PageSize = 2;
        var marked = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var id = await ChargeInReview();
            await MakeDue(id, TimeSpan.FromMinutes(3));
            (
                await RawSql.MarkEffect(
                    _host,
                    id,
                    nameof(TimedChargeTrigger.Charge),
                    $"charge-{i}",
                    (await RawSql.Now(_host)).AddMinutes(5)
                )
            )
                .Should()
                .Be(1);
            marked.Add(id);
        }
        var behind = await Reminder();
        await MakeDue(behind, TimeSpan.FromMinutes(1));

        try
        {
            var results = await Reconciler.FireDueTimersOnce(CancellationToken.None);

            results
                .Should()
                .ContainSingle(r => r.InstanceId == behind)
                .Which.Outcome.Should()
                .Be(
                    TimerFireResult.Fired,
                    $"rows with a pending effect never fill the sweep's batch. See {Adr}"
                );
            results.Should().NotContain(r => marked.Contains(r.InstanceId));
            foreach (var id in marked)
                (await RawSql.Timer(_host, id))!.State.Should().Be(nameof(TimedChargeState.Review));
        }
        finally
        {
            foreach (var id in marked)
                await RawSql.Execute(
                    _host,
                    $"UPDATE {RawSql.Table(_host)} SET effect_token = NULL, effect_key = NULL, "
                        + "effect_trigger = NULL, effect_request_id = NULL, effect_lease_until = NULL "
                        + $"WHERE row_id = {await _host.RowId(id)}"
                );
        }
    }

    [Test]
    public async Task An_effect_sent_while_a_fire_is_being_written_never_runs()
    {
        var id = await ChargeInReview();
        await MakeDue(id, TimeSpan.FromMinutes(1));
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Reconciler.AfterClaimCheck = async row =>
        {
            if (row.Id != id)
                return;
            paused.TrySetResult();
            await release.Task;
        };

        // The sweep holds the row and is about to write the timer's move; the send loads the draft and tries to mark
        // it, which the held row and the fire's new revision both refuse.
        var sweep = Task.Run(() => Reconciler.FireDueTimersOnce(CancellationToken.None));
        await paused.Task.WaitAsync(Bound);
        var send = Task.Run(async () =>
        {
            using var scope = _host.Scope();
            return await _host
                .Service(scope, TimedChargeMachine.Id)
                .Send(new Actor.Client(User), id, "charge-1");
        });
        release.TrySetResult();

        var fired = await sweep.WaitAsync(Bound);
        var sent = await send.WaitAsync(Bound);

        fired
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Fired);
        _charge
            .Calls.Should()
            .Be(0, $"the effect never runs on a state the timer has left: never both. See {Adr}");
        (await RawSql.Timer(_host, id))!.State.Should().Be(nameof(TimedChargeState.Expired));
        sent.Should()
            .BeOfType<AdvanceOutcome.Rejected>(
                "the send meets the instance the timer left, where the effect has no edge"
            )
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [Test]
    public async Task A_timer_due_while_an_effect_is_pending_waits_for_its_record()
    {
        var id = await ChargeInReview();
        await MakeDue(id, TimeSpan.FromMinutes(1));
        _charge.Script(ChargeRun.Hold);
        var send = Task.Run(async () =>
        {
            using var scope = _host.Scope();
            return await _host
                .Service(scope, TimedChargeMachine.Id)
                .Send(new Actor.Client(User), id, "charge-1");
        });
        await _charge.Entered.WaitAsync(Bound);

        var fired = await Reconciler.FireDueTimersOnce(CancellationToken.None);
        var whilePending = (await RawSql.Timer(_host, id))!.State;
        _charge.Release();
        var sent = await send.WaitAsync(Bound);

        fired
            .Should()
            .NotContain(
                r => r.InstanceId == id,
                $"a row with a pending effect is passed over until the effect is recorded. See {Adr}"
            );
        whilePending.Should().Be(nameof(TimedChargeState.Review));
        sent.Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be(nameof(TimedChargeState.Charged));
        _charge.Calls.Should().Be(1);
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .NotContain(r => r.InstanceId == id);
    }

    [Test]
    public async Task A_receipt_the_earlier_ledger_recorded_holds_the_timer_until_a_send_records_it()
    {
        var id = await ChargeInReview();
        await MakeDue(id, TimeSpan.FromMinutes(1));
        string fingerprint;
        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, TimedChargeMachine.Id);
            var loaded = (await service.Load(User, id))
                .Should()
                .BeOfType<LoadResult.Loaded>()
                .Subject;
            fingerprint = SnapshotFingerprint.Of(service.Serialize(loaded.Snapshot));
            var claims = scope.ServiceProvider.GetRequiredService<IEffectClaimStore>();
            foreach (var key in EffectKeys(id))
            {
                var won = (await claims.TryClaim(key, TimeSpan.FromMinutes(5), fingerprint))
                    .Should()
                    .BeOfType<ClaimResult.Won>()
                    .Subject;
                (await claims.Complete(key, won.OwnerToken, "receipt-earlier")).Should().BeTrue();
            }
        }

        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(
                TimerFireResult.Waiting,
                $"the timer never fires past a receipt the instance has not recorded. See {Adr}"
            );

        using (var scope = _host.Scope())
            (
                await _host
                    .Service(scope, TimedChargeMachine.Id)
                    .Send(new Actor.Client(User), id, "charge-1")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Which.Snapshot.State.Should()
                .Be(nameof(TimedChargeState.Charged), "the next send records the earlier receipt");
        _charge.Calls.Should().Be(0, "the receipt is recorded, the effect does not run again");
        (await RawSql.Timer(_host, id))!.State.Should().Be(nameof(TimedChargeState.Charged));
    }

    [Test]
    public async Task An_effect_past_its_lease_is_finished_before_its_timer_fires()
    {
        var id = await ChargeInReview();
        await MakeDue(id, TimeSpan.FromMinutes(1));
        (
            await RawSql.MarkEffect(
                _host,
                id,
                nameof(TimedChargeTrigger.Charge),
                "charge-1",
                (await RawSql.Now(_host)).AddMinutes(-1)
            )
        )
            .Should()
            .Be(1);

        var fired = await Reconciler.FireDueTimersOnce(CancellationToken.None);

        fired.Should().NotContain(r => r.InstanceId == id && r.Moved);
        (await RawSql.Timer(_host, id))!
            .State.Should()
            .Be(
                nameof(TimedChargeState.Charged),
                $"the sweep finishes an effect its claimant left, and the timer never fires past it. See {Adr}"
            );
        _charge.KeysOf(id).Should().ContainSingle();
    }

    [Test]
    public async Task A_receipt_for_other_content_does_not_hold_the_timer()
    {
        var id = await ChargeInReview();
        await MakeDue(id, TimeSpan.FromMinutes(1));
        using (var scope = _host.Scope())
        {
            var claims = scope.ServiceProvider.GetRequiredService<IEffectClaimStore>();
            foreach (var key in EffectKeys(id))
            {
                var won = (await claims.TryClaim(key, TimeSpan.FromMinutes(5), "other-content"))
                    .Should()
                    .BeOfType<ClaimResult.Won>()
                    .Subject;
                (await claims.Complete(key, won.OwnerToken, "receipt-elsewhere")).Should().BeTrue();
            }
        }

        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(
                TimerFireResult.Fired,
                "a receipt no send would apply to this content never holds the timer"
            );
        (await RawSql.Timer(_host, id))!.State.Should().Be(nameof(TimedChargeState.Expired));
    }

    /// <summary>A clock that moves only when the test moves it.</summary>
    private sealed class SteppingTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Counts what one state-machine instrument reports while it is alive.</summary>
    private sealed class Counted : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _count;

        public Counted(string instrumentName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (
                    instrument.Meter.Name == StateMachineInstruments.MeterName
                    && instrument.Name == instrumentName
                )
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, value, _, _) => Interlocked.Add(ref _count, value)
            );
            _listener.Start();
        }

        public long Count => Interlocked.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
