using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// Where a row's <c>due_at</c> is written and cleared: every path that enters a timed state sets it to the
/// database's clock plus the state's duration, in the same statement that writes the state, and every path that
/// leaves one clears it. Each write bumps the revision by one, and a refused event writes neither. The columns are
/// read by raw SQL, the way the sweep reads them.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class TimerDueAtTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "timer-user";

    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    private InstanceHost? _host;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider, launcher: new RecordingLauncher());

    [TearDown]
    public void TearDown() => _host?.Dispose();

    private static Snapshot Reminder(string state) =>
        new()
        {
            Machine = ReminderMachine.Id,
            Version = 1,
            State = state,
            Context = new JsonObject(),
        };

    private static Snapshot Waiting(string state, TimeSpan patience) =>
        new()
        {
            Machine = WaitMachine.Id,
            Version = 1,
            State = state,
            Context = WaitMachine.Context(patience),
        };

    private static async Task<Guid> InReview(InstanceHost host)
    {
        var id = Guid.NewGuid();
        await host.SeedDraft(User, id, ReminderMachine.Json("Idle")).ConfigureAwait(false);
        using var scope = host.Scope();
        var service = host.Service(scope, ReminderMachine.Id);
        (await service.Advance(new Actor.Client(User, "Editor"), id, "Open").ConfigureAwait(false))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Review");
        return id;
    }

    private async Task<Guid> InFetching(TimeSpan patience)
    {
        var id = Guid.NewGuid();
        await _host
            .NotNull()
            .SeedDraft(User, id, WaitMachine.Json("Idle", patience))
            .ConfigureAwait(false);
        using var scope = _host.NotNull().Scope();
        var service = _host.NotNull().Service(scope, WaitMachine.Id);
        (await service.Advance(User, id, "Fetch").ConfigureAwait(false))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Fetching");
        return id;
    }

    private static async Task<TimerRow> Row(InstanceHost host, Guid id) =>
        (await RawSql.Timer(host, id).ConfigureAwait(false))
        ?? throw new InvalidOperationException($"No row holds the id {id}.");

    private static async Task<TimerRow> ShouldBeDue(
        InstanceHost host,
        Guid id,
        string state,
        TimeSpan after
    )
    {
        var now = await RawSql.Now(host).ConfigureAwait(false);
        var row = await Row(host, id).ConfigureAwait(false);
        row.State.Should().Be(state);
        row.DueAt.Should()
            .NotBeNull($"the row is in the timed state {state}. See {Adr}")
            .And.BeCloseTo(
                now + after,
                Tolerance,
                $"due_at is the database's clock plus the state's duration. See {Adr}"
            );
        return row;
    }

    private static async Task<TimerRow> ShouldHaveNoTimer(InstanceHost host, Guid id, string state)
    {
        var row = await Row(host, id).ConfigureAwait(false);
        row.State.Should().Be(state);
        row.DueAt.Should().BeNull($"{state} has no timer. See {Adr}");
        return row;
    }

    private async Task<string> InvokeToken(Guid id) =>
        (await _host.NotNull().Rows(id).ConfigureAwait(false)).Single().InvokeToken
        ?? throw new InvalidOperationException("The draft holds no run.");

    private async Task EndRun(string token, bool failed)
    {
        using var scope = _host.NotNull().Scope();
        var entries = scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.Where(w => w.ExternalId == token);
        // A run whose records are gone ends as a failure; a cancelled entry ends as a cancel.
        if (failed)
            await entries.ExecuteDeleteAsync().ConfigureAwait(false);
        else
            await entries
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkQueueStatus.Cancelled))
                .ConfigureAwait(false);
    }

    private InvokeOutcomeReconciler Reconciler =>
        _host.NotNull().Services.GetRequiredService<InvokeOutcomeReconciler>();

    [Test]
    public async Task Start_into_a_timed_initial_state_sets_due_at()
    {
        Guid id;
        using (var scope = _host.NotNull().Scope())
            id = (
                await _host
                    .NotNull()
                    .Service(scope, ExpiringMachine.Id)
                    .Start(new Actor.Client(User), input: null, "start-1")
            )
                .Should()
                .BeOfType<StartOutcome.Started>()
                .Which.Id;

        var row = await ShouldBeDue(_host.NotNull(), id, "Open", ExpiringMachine.OpenFor);
        row.Revision.Should().Be(1);
    }

    [Test]
    public async Task Advance_into_a_timed_state_sets_due_at()
    {
        var id = Guid.NewGuid();
        await _host.NotNull().SeedDraft(User, id, ReminderMachine.Json("Idle"));
        var before = await ShouldHaveNoTimer(_host.NotNull(), id, "Idle");

        using (var scope = _host.NotNull().Scope())
            (
                await _host
                    .NotNull()
                    .Service(scope, ReminderMachine.Id)
                    .Advance(new Actor.Client(User, "Editor"), id, "Open")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        var after = await ShouldBeDue(_host.NotNull(), id, "Review", ReminderMachine.ReviewFor);
        after.Revision.Should().Be(before.Revision + 1);
    }

    [Test]
    public async Task Advance_out_of_a_timed_state_clears_due_at()
    {
        var id = await InReview(_host.NotNull());
        var before = await ShouldBeDue(_host.NotNull(), id, "Review", ReminderMachine.ReviewFor);

        using (var scope = _host.NotNull().Scope())
            (
                await _host
                    .NotNull()
                    .Service(scope, ReminderMachine.Id)
                    .Advance(User, id, "Approve", new JsonObject { ["verdict"] = "approved" })
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        var after = await ShouldHaveNoTimer(_host.NotNull(), id, "Done");
        after.Revision.Should().Be(before.Revision + 1);
    }

    [Test]
    public async Task Self_entry_through_Extend_restarts_the_timer()
    {
        var id = await InReview(_host.NotNull());
        await RawSql.SetDueAt(
            _host.NotNull(),
            id,
            (await RawSql.Now(_host.NotNull())).AddMinutes(-1)
        );
        var before = await Row(_host.NotNull(), id);

        using (var scope = _host.NotNull().Scope())
            (await _host.NotNull().Service(scope, ReminderMachine.Id).Advance(User, id, "Extend"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        var after = await ShouldBeDue(_host.NotNull(), id, "Review", ReminderMachine.ReviewFor);
        after.Revision.Should().Be(before.Revision + 1);
    }

    [Test]
    public async Task A_rejected_event_writes_nothing()
    {
        var id = await InReview(_host.NotNull());
        var before = await Row(_host.NotNull(), id);

        using (var scope = _host.NotNull().Scope())
            (
                await _host
                    .NotNull()
                    .Service(scope, ReminderMachine.Id)
                    .Advance(User, id, "Approve", new JsonObject { ["verdict"] = "rejected" })
            )
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>();

        (await Row(_host.NotNull(), id))
            .Should()
            .Be(before, $"a refused event writes neither due_at nor the revision. See {Adr}");
    }

    [Test]
    public async Task An_outcome_entering_a_timed_state_sets_due_at_by_hook_and_by_sweep()
    {
        var byHook = await InFetching(TimeSpan.FromMinutes(5));
        var bySweep = await InFetching(TimeSpan.FromMinutes(5));
        var hookBefore = await ShouldBeDue(
            _host.NotNull(),
            byHook,
            "Fetching",
            TimeSpan.FromMinutes(5)
        );
        var sweepBefore = await ShouldBeDue(
            _host.NotNull(),
            bySweep,
            "Fetching",
            TimeSpan.FromMinutes(5)
        );
        var hookToken = await InvokeToken(byHook);
        await EndRun(hookToken, failed: true);
        await EndRun(await InvokeToken(bySweep), failed: true);

        (
            await _host
                .NotNull()
                .Services.GetRequiredService<IInvokedRunOutcomes>()
                .Deliver(hookToken)
        )
            .Should()
            .Be("Cooldown");
        await Reconciler.SweepOnce(CancellationToken.None);

        (await ShouldBeDue(_host.NotNull(), byHook, "Cooldown", WaitMachine.CooldownFor))
            .Revision.Should()
            .Be(hookBefore.Revision + 1);
        (await ShouldBeDue(_host.NotNull(), bySweep, "Cooldown", WaitMachine.CooldownFor))
            .Revision.Should()
            .Be(sweepBefore.Revision + 1);
    }

    [Test]
    public async Task An_outcome_leaving_a_timed_invoking_state_clears_due_at()
    {
        var id = await InFetching(TimeSpan.FromMinutes(5));
        var before = await ShouldBeDue(_host.NotNull(), id, "Fetching", TimeSpan.FromMinutes(5));
        var token = await InvokeToken(id);
        await EndRun(token, failed: false);

        (await _host.NotNull().Services.GetRequiredService<IInvokedRunOutcomes>().Deliver(token))
            .Should()
            .Be("Stopped");

        (await ShouldHaveNoTimer(_host.NotNull(), id, "Stopped"))
            .Revision.Should()
            .Be(before.Revision + 1);
    }

    [Test]
    public async Task Send_after_effect_into_a_timed_state_sets_due_at()
    {
        var id = Guid.NewGuid();
        await _host.NotNull().SeedDraft(User, id, RenewalMachine.DraftJson);
        var before = await ShouldHaveNoTimer(_host.NotNull(), id, "Draft");

        using (var scope = _host.NotNull().Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .EffectRunner(RenewalMachine.Id)
                    .NotNull()
                    .Run(User, id, "charge-1")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Which.Snapshot.State.Should()
                .Be("Active");

        (await ShouldBeDue(_host.NotNull(), id, "Active", RenewalMachine.ActiveFor))
            .Revision.Should()
            .Be(before.Revision + 2, "the send marks the row, then records the effect");
    }

    [Test]
    public async Task System_start_and_advance_set_due_at()
    {
        var key = MachineKey.Of("timers", Guid.NewGuid().ToString());
        var instance = await _host.NotNull().Start<SystemReminderMachine>(key);
        var before = await ShouldHaveNoTimer(_host.NotNull(), instance.Id, "Idle");

        using (var scope = _host.NotNull().Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Advance<SystemReminderMachine>(key, "Open")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        (await ShouldBeDue(_host.NotNull(), instance.Id, "Review", ReminderMachine.ReviewFor))
            .Revision.Should()
            .Be(before.Revision + 1);
    }

    [Test]
    public async Task A_timer_firing_into_another_timed_state_sets_its_due_at()
    {
        var id = await InReview(_host.NotNull());
        await RawSql.SetDueAt(
            _host.NotNull(),
            id,
            (await RawSql.Now(_host.NotNull())).AddMinutes(-1)
        );
        var before = await Row(_host.NotNull(), id);

        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Fired);

        (await ShouldBeDue(_host.NotNull(), id, "Expired", ReminderMachine.ExpiredFor))
            .Revision.Should()
            .Be(before.Revision + 1);
    }

    [Test]
    public async Task A_strand_keeps_due_at_and_the_timer_still_leaves()
    {
        var id = await InFetching(TimeSpan.FromMinutes(5));
        var before = await ShouldBeDue(_host.NotNull(), id, "Fetching", TimeSpan.FromMinutes(5));
        var row = (await _host.NotNull().Rows(id)).Single();

        using (var scope = _host.NotNull().Scope())
            (
                await _host
                    .NotNull()
                    .Instances(scope)
                    .StrandByInvokeToken(
                        row.InvokeToken.NotNull(),
                        Waiting("Fetching", TimeSpan.FromMinutes(5)),
                        row.ConcurrencyToken
                    )
            )
                .Should()
                .BeTrue();

        var stranded = await Row(_host.NotNull(), id);
        stranded
            .DueAt.Should()
            .Be(before.DueAt, $"a strand leaves the state, and so its timer, in place. See {Adr}");

        await RawSql.SetDueAt(
            _host.NotNull(),
            id,
            (await RawSql.Now(_host.NotNull())).AddMinutes(-1)
        );
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Fired);
        await ShouldHaveNoTimer(_host.NotNull(), id, "TimedOut");
    }

    [Test]
    public async Task A_timer_leaves_an_invoking_state_whose_run_nothing_dispatched()
    {
        // This host registers no scheduler: the run is queued and nothing takes it. The timer is fired by the
        // outcome reconciler, which every host with machines runs, so the instance still leaves the state, and the
        // queued run is cancelled with it.
        var id = await InFetching(TimeSpan.FromMinutes(5));
        var token = await InvokeToken(id);

        await RawSql.SetDueAt(
            _host.NotNull(),
            id,
            (await RawSql.Now(_host.NotNull())).AddMinutes(-1)
        );
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Fired);

        await ShouldHaveNoTimer(_host.NotNull(), id, "TimedOut");
        (await _host.NotNull().Rows(id)).Single().InvokeToken.Should().BeNull();
        using var scope = _host.NotNull().Scope();
        (
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .WorkQueues.Where(w => w.ExternalId == token)
                .Select(w => w.Status)
                .SingleAsync()
        )
            .Should()
            .Be(WorkQueueStatus.Cancelled, $"leaving an invoking state cancels its run. See {Adr}");
    }

    [Test]
    public async Task Upsert_through_the_public_store_into_a_timed_state_sets_due_at()
    {
        var id = Guid.NewGuid();

        using (var scope = _host.NotNull().Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Upsert(User, id, Reminder("Review"))
            )
                .Should()
                .BeTrue();

        await ShouldBeDue(_host.NotNull(), id, "Review", ReminderMachine.ReviewFor);
    }

    [Test]
    public async Task Duration_is_read_at_entry_not_at_fire()
    {
        var id = await InFetching(TimeSpan.FromMinutes(5));
        var before = await ShouldBeDue(_host.NotNull(), id, "Fetching", TimeSpan.FromMinutes(5));
        var row = (await _host.NotNull().Rows(id)).Single();

        using (var scope = _host.NotNull().Scope())
            (
                await _host
                    .NotNull()
                    .Instances(scope)
                    .Update(
                        DraftOwner.User(User),
                        id,
                        Waiting("Fetching", TimeSpan.FromMinutes(30)),
                        row.ConcurrencyToken,
                        request: null
                    )
            )
                .Should()
                .BeTrue();

        (await Row(_host.NotNull(), id))
            .DueAt.Should()
            .Be(
                before.DueAt,
                $"a write that does not enter the state again keeps the deadline set at entry. See {Adr}"
            );
    }

    [Test]
    public async Task due_at_comes_from_the_database_clock()
    {
        // The database's clock runs an hour ahead of this host's, as the host's data contexts read it.
        using var skewed = InstanceHost.Create(
            provider,
            launcher: new RecordingLauncher(),
            services: SkewedDatabaseClock.Install
        );

        var id = await InReview(skewed);

        var hostNow = await RawSql.Now(skewed);
        (await Row(skewed, id))
            .DueAt.Should()
            .NotBeNull()
            .And.BeCloseTo(
                hostNow + SkewedDatabaseClock.Ahead + ReminderMachine.ReviewFor,
                Tolerance,
                $"due_at is armed from the database's clock, never this host's. See {Adr}"
            );
    }

    [Test]
    public async Task due_at_is_armed_from_when_it_is_written_not_from_when_its_transaction_began()
    {
        var id = Guid.NewGuid();
        DateTimeOffset began;
        DateTimeOffset later;
        using (var scope = _host.NotNull().Scope())
        {
            var services = scope.ServiceProvider;
            var context = await services
                .GetRequiredService<IDataContextProviderFactory>()
                .CreateDbContextAsync(CancellationToken.None);
            await using var contextDisposal = context.ConfigureAwait(false);
            var database = ((DbContext)context).Database;
            var store = services
                .GetRequiredService<InvokeOutbox>()
                .StoreOver(context, services.GetService<ISqlDialect>());

            using var transaction = await context.BeginTransaction(CancellationToken.None);
            // On Postgres now() is the transaction's start for as long as it lasts; the statement clock moves on.
            began = await Clock(database, transactionStart: true);
            var deadline = DateTimeOffset.UtcNow + Bound;
            while ((later = await Clock(database, transactionStart: false)) <= began)
                DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the statement clock moves on");

            (await store.Upsert(User, id, Reminder("Review"))).Should().BeTrue();
            await transaction.Commit();
        }

        (await Row(_host.NotNull(), id))
            .DueAt.Should()
            .NotBeNull()
            .And.BeOnOrAfter(
                later + ReminderMachine.ReviewFor,
                $"due_at is armed on the clock of the statement that writes it, not the transaction's start. See {Adr}"
            );
    }

    // The database's clock as Postgres's now() (the transaction's start) or clock_timestamp() (the statement's) reads
    // it; SQLite has only the statement's.
    private async Task<DateTimeOffset> Clock(DatabaseFacade database, bool transactionStart)
    {
        var sql =
            provider == StoreProvider.Postgres
                ? $"SELECT to_char({(transactionStart ? "now()" : "clock_timestamp()")} AT TIME ZONE 'UTC', "
                    + "'YYYY-MM-DD\"T\"HH24:MI:SS.US') AS \"Value\""
                : "SELECT strftime('%Y-%m-%dT%H:%M:%f', 'now') AS \"Value\"";
        var text = await database.SqlQueryRaw<string>(sql).SingleAsync().ConfigureAwait(false);
        return DateTimeOffset.Parse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
        );
    }

    [Test]
    public async Task due_at_is_written_in_the_same_statement_as_the_state()
    {
        var gate = new WriteGate();
        using var paused = InstanceHost.Create(
            provider,
            launcher: new RecordingLauncher(),
            services: WriteGate.Install(gate)
        );
        var id = Guid.NewGuid();
        await paused.SeedDraft(User, id, ReminderMachine.Json("Idle"));

        gate.Arm(PausePoint.AfterWrite);
        var advance = Task.Run(async () =>
        {
            using var scope = paused.Scope();
            return await paused
                .Service(scope, ReminderMachine.Id)
                .Advance(new Actor.Client(User, "Editor"), id, "Open")
                .ConfigureAwait(false);
        });
        try
        {
            await gate.Reached.WaitAsync(Bound);

            var between = await Row(paused, id);
            if (string.Equals(between.State, "Review", StringComparison.Ordinal))
                between
                    .DueAt.Should()
                    .NotBeNull(
                        $"no reader ever sees the timed state without its deadline. See {Adr}"
                    );
        }
        finally
        {
            gate.Release();
        }

        (await advance.WaitAsync(Bound)).Should().BeOfType<AdvanceOutcome.Advanced>();
        await ShouldBeDue(paused, id, "Review", ReminderMachine.ReviewFor);
    }

    [Test]
    public async Task due_at_is_utc_and_orders_correctly_on_sqlite()
    {
        var later = await InReview(_host.NotNull());
        var sooner = Guid.NewGuid();
        await _host.NotNull().SeedDraft(User, sooner, PlainTimerMachine.Json("Idle"));
        using (var scope = _host.NotNull().Scope())
        {
            var service = _host.NotNull().Service(scope, PlainTimerMachine.Id);
            (await service.Advance(User, sooner, "Wait"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        }

        var table = RawSql.Table(_host.NotNull());
        var ordered = await RawSql.Query(
            _host.NotNull(),
            $"SELECT id, due_at FROM {table} WHERE due_at IS NOT NULL AND (id = @a OR id = @b) ORDER BY due_at",
            (
                "@a",
                provider == StoreProvider.Postgres ? later : later.ToString().ToUpperInvariant()
            ),
            (
                "@b",
                provider == StoreProvider.Postgres ? sooner : sooner.ToString().ToUpperInvariant()
            )
        );

        ordered
            .Select(r => Guid.Parse(r[0].NotNull().ToString().NotNull()))
            .Should()
            .Equal(
                [sooner, later],
                $"the five-minute timer is due before the ten-minute one, in the database's order. See {Adr}"
            );
        foreach (var row in ordered)
            if (row[1] is string text)
                text.Should()
                    .EndWith(
                        "+00:00",
                        "SQLite stores due_at as UTC text, so text order is time order"
                    );
    }
}
