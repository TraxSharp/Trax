using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fakes.Timers;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.Timers;

/// <summary>
/// A state's timer fires once it is due, on whichever host sweeps it first, by a conditional update on the row's
/// state and revision: so a due timer moves its instance exactly once across hosts, never before it is due on the
/// database's clock, never under a live effect claim, and never alongside a client event, an outcome, an operator's
/// cancel, a delete or an expiry that got there first. A timer leaving an invoking state cancels its run through the
/// outbox's leaving path. Time moves by backdating <c>due_at</c> in SQL; every sweep is a call the test makes. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
/// <remarks>
/// A race between a fire and a client event is made deterministic in both orders: <see cref="PausingSnapshotStore"/>
/// holds the client's write between its read and its update while the test fires the timer, and the reconciler's
/// <c>AfterRead</c> hook holds the fire between its read and its write while the client writes.
/// </remarks>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TimerFiringTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    // Every wait in this class: long past any step here, short enough that a hang fails the test.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly PauseGate _gate = new();

    private InvokeCluster? _cluster;
    private TimerDb? _db;

    // Two API hosts with every machine of the assembly, the reminder's effect among them.
    private ClusterHost? _api;
    private ClusterHost? _api2;

    // Dispatches and runs trains, and registers no machines, so nothing it runs is delivered by a hook.
    private ClusterHost? _worker;

    // Dispatches and runs trains and registers the machines, so its hook delivers each outcome at once.
    private ClusterHost? _full;

    // Registers only the reminder, which has a timer and invokes nothing.
    private ClusterHost? _timerOnly;

    // Registers every machine but the reminder.
    private ClusterHost? _noReminder;

    // An API host whose draft writes the test can hold between their read and their update.
    private ClusterHost? _pausing;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _db = new TimerDb(_cluster.NotNull());
        _api = _cluster.NotNull().Host(machines: true, scheduler: false, TimerHosts.Publishing);
        _api2 = _cluster.NotNull().Host(machines: true, scheduler: false, TimerHosts.Publishing);
        _worker = _cluster.NotNull().Host(machines: false, scheduler: true);
        _full = _cluster.NotNull().Host(machines: true, scheduler: true, TimerHosts.Publishing);
        _timerOnly = _cluster
            .NotNull()
            .Host(
                machines: true,
                scheduler: false,
                TimerHosts.All(TimerHosts.Publishing, TimerHosts.Only(typeof(ReminderMachine)))
            );
        _noReminder = _cluster
            .NotNull()
            .Host(machines: true, scheduler: false, TimerHosts.Without(typeof(ReminderMachine)));
        _pausing = _cluster
            .NotNull()
            .Host(
                machines: true,
                scheduler: false,
                TimerHosts.All(TimerHosts.Publishing, TimerHosts.Pausing(_gate))
            );
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        if (_api is not null)
            await _api.DisposeAsync();
        if (_api2 is not null)
            await _api2.DisposeAsync();
        if (_worker is not null)
            await _worker.DisposeAsync();
        if (_full is not null)
            await _full.DisposeAsync();
        if (_timerOnly is not null)
            await _timerOnly.DisposeAsync();
        if (_noReminder is not null)
            await _noReminder.DisposeAsync();
        if (_pausing is not null)
            await _pausing.DisposeAsync();
        if (_cluster is not null)
            await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        ReminderPublishGate.Reset();
        _gate.Reset();
        await _cluster.NotNull().Reset();
    }

    [TearDown]
    public void Restore()
    {
        InvokedStepGate.Release();
        ReminderPublishGate.Reset();
        _gate.Reset();
        foreach (
            var host in new[]
            {
                _api.NotNull(),
                _api2.NotNull(),
                _full.NotNull(),
                _timerOnly.NotNull(),
                _noReminder.NotNull(),
                _pausing.NotNull(),
            }
        )
        {
            host.Reconciler.PageSize = 200;
            host.Reconciler.Time = TimeProvider.System;
            host.Reconciler.AfterRead = null;
        }
    }

    #region A4: firing

    [Test]
    public async Task A_due_timer_fires_once_across_two_hosts()
    {
        var (_, id, row) = await OpenReview();
        // Backdating due_at is a write too, which the database numbers, so the revision is read after it.
        await _db.NotNull().MakeDue(row);
        var before = await _db.NotNull().Revision(row);

        var fires = await Task.WhenAll(_api.NotNull().FireTimers(), _api2.NotNull().FireTimers())
            .WaitAsync(Bound);

        var mine = fires.SelectMany(f => f).Where(r => r.InstanceId == id).ToList();
        mine.Count(r => string.Equals(r.Outcome, TimerFireResult.Fired, StringComparison.Ordinal))
            .Should()
            .Be(1, $"a due timer is applied by one conditional update, on one host. See {Adr}");
        mine.Where(r => !string.Equals(r.Outcome, TimerFireResult.Fired, StringComparison.Ordinal))
            .Should()
            .OnlyContain(r => r.Outcome == TimerFireResult.Lost, "the other host lost the race");
        var stored = await Reminder(id);
        stored.State.Should().Be(nameof(ReminderState.Expired));
        (await _db.NotNull().Revision(row)).Should().Be(before + 1, "the fire is one write");
        (await _db.NotNull().DueAt(row)).Should().BeNull("Expired has no timer");
    }

    [Test]
    public async Task A_timer_not_yet_due_does_not_fire()
    {
        var (_, id, row) = await OpenReview();
        var due = await _db.NotNull().DueAt(row);
        due.Should()
            .NotBeNull($"entering Review arms its timer. See {Adr}")
            .And.BeAfter(await _db.NotNull().Now());

        (await _api.NotNull().FireTimers()).Should().NotContain(r => r.InstanceId == id);
        (await _api2.NotNull().FireTimers()).Should().NotContain(r => r.InstanceId == id);

        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Review));
        (await _db.NotNull().DueAt(row)).Should().Be(due);
    }

    [Test]
    public async Task A_timer_only_machine_is_swept()
    {
        var user = NewUser();
        var id = Guid.NewGuid();
        (await _timerOnly.NotNull().OpenReview(user, id))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var row = (await _timerOnly.NotNull().Row(id, ReminderMachine.MachineId)).NotNull().RowId;
        await _db.NotNull().MakeDue(row);

        (await _timerOnly.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(
                TimerFireResult.Fired,
                $"a host whose machines have timers sweeps them, though none invokes a train. See {Adr}"
            );
        (await _timerOnly.NotNull().Row(id, ReminderMachine.MachineId))
            .NotNull()
            .State.Should()
            .Be(nameof(ReminderState.Expired));
    }

    [Test]
    public async Task Concurrent_sweeps_take_disjoint_SKIP_LOCKED_batches()
    {
        const int count = 50;
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var (_, id, _) = await OpenReview();
            ids.Add(id);
        }
        await _db.NotNull().MakeAllDue(ReminderMachine.MachineId);
        _api.NotNull().Reconciler.PageSize = 10;
        _api2.NotNull().Reconciler.PageSize = 10;

        // Each host sweeps until a sweep fires nothing; neither may wait on the other's locks.
        var results = await Task.WhenAll(Drain(_api.NotNull()), Drain(_api2.NotNull()))
            .WaitAsync(Bound);

        var fired = results
            .SelectMany(r => r)
            .Where(r => string.Equals(r.Outcome, TimerFireResult.Fired, StringComparison.Ordinal))
            .ToList();
        fired
            .Select(r => r.InstanceId)
            .Should()
            .BeEquivalentTo(
                ids,
                $"each due row is fired by exactly one host, in batches of ten that never overlap. See {Adr}"
            );
        foreach (var id in ids)
            (await Reminder(id)).State.Should().Be(nameof(ReminderState.Expired));

        static async Task<List<TimerFireResult>> Drain(ClusterHost host)
        {
            var all = new List<TimerFireResult>();
            while (true)
            {
                var batch = await host.FireTimers().ConfigureAwait(false);
                if (
                    !batch.Any(r =>
                        string.Equals(r.Outcome, TimerFireResult.Fired, StringComparison.Ordinal)
                    )
                )
                    return all;
                all.AddRange(batch);
            }
        }
    }

    [Test]
    public async Task A_host_not_registering_the_machine_leaves_the_row()
    {
        var (_, id, row) = await OpenReview();
        await _db.NotNull().MakeDue(row);
        var due = await _db.NotNull().DueAt(row);
        var revision = await _db.NotNull().Revision(row);
        _noReminder.NotNull().Reconciler.Time = new ManualTime(DateTimeOffset.UtcNow);

        (await _noReminder.NotNull().FireTimers())
            .Should()
            .NotContain(
                r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired,
                $"a host that cannot read the machine leaves its timer to one that can. See {Adr}"
            );
        (await _noReminder.NotNull().FireTimers())
            .Should()
            .NotContain(r => r.InstanceId == id, "the row waits before this host tries it again");

        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Review));
        (await _db.NotNull().DueAt(row)).Should().Be(due);
        (await _db.NotNull().Revision(row)).Should().Be(revision);

        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);
    }

    [Test]
    public async Task The_hosted_reconciler_wakes_for_the_next_due_timer_before_its_interval()
    {
        var waking = _cluster
            .NotNull()
            .Host(
                machines: true,
                scheduler: false,
                TimerHosts.Publishing,
                options: o =>
                {
                    o.InvokeOutcomeSweepInterval = TimeSpan.FromHours(1);
                    o.InvokeOutcomeSweepIntervalWhileListening = TimeSpan.FromHours(1);
                }
            );
        await using var wakingDisposal = waking.ConfigureAwait(false);
        var (_, id, row) = await OpenReview();
        await _db.NotNull().DueIn(row, TimeSpan.FromSeconds(1));

        await waking.Reconciler.StartAsync(CancellationToken.None);
        try
        {
            await waking.Reconciler.FirstSweep.WaitAsync(Bound);

            // Polls the condition, under a ceiling (ADR 0006): no sweep is due for an hour, so only a wake for the
            // timer's due time can move the row.
            var elapsed = Stopwatch.StartNew();
            while (
                !string.Equals(
                    (await Reminder(id)).State,
                    nameof(ReminderState.Expired),
                    StringComparison.Ordinal
                )
                && elapsed.Elapsed < Bound
            )
                await Task.Yield();
        }
        finally
        {
            await waking.Reconciler.StopAsync(CancellationToken.None);
        }

        (await Reminder(id))
            .State.Should()
            .Be(
                nameof(ReminderState.Expired),
                $"the reconciler wakes for the next due timer, not at its hourly interval. See {Adr}"
            );
    }

    [Test]
    public async Task A_host_whose_clock_runs_ahead_does_not_fire_early()
    {
        var (_, id, row) = await OpenReview();
        var due = await _db.NotNull().DueAt(row);
        due.Should().NotBeNull();
        _api2.NotNull().Reconciler.Time = new ManualTime(
            DateTimeOffset.UtcNow + TimeSpan.FromHours(1)
        );

        (await _api2.NotNull().FireTimers())
            .Should()
            .NotContain(
                r => r.InstanceId == id,
                $"whether a timer is due is read on the database's clock, never a host's. See {Adr}"
            );
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Review));
        (await _db.NotNull().DueAt(row)).Should().Be(due);
    }

    [Test]
    public async Task A_fire_that_cannot_be_applied_clears_due_at_and_is_metered()
    {
        var (_, id, row) = await OpenReview();
        await _db.NotNull().SetContext(row, new JsonObject { ["note"] = "", ["blocked"] = true });
        await _db.NotNull().MakeDue(row);
        using var failures = new FailedFires();

        (await _api.NotNull().FireTimers())
            .Should()
            .NotContain(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);

        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Review));
        (await _db.NotNull().DueAt(row))
            .Should()
            .BeNull($"a fire that cannot be applied is cleared, not retried forever. See {Adr}");
        failures.Count.Should().Be(1, "the failed fire is counted");

        (await _api.NotNull().FireTimers()).Should().NotContain(r => r.InstanceId == id);
        failures.Count.Should().Be(1, "nothing is tried again");
    }

    [Test]
    public async Task A_due_timer_waits_for_a_live_effect_claim()
    {
        var (user, id, row) = await OpenReview();
        ReminderPublishGate.Armed = true;
        var publish = _api.NotNull().Publish(user, id, "publish-1");
        await ReminderPublishGate.Entered.WaitAsync(Bound);
        await _db.NotNull().MakeDue(row);

        (await _api2.NotNull().FireTimers())
            .Should()
            .NotContain(
                r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired,
                $"a timer never fires under a live effect claim. See {Adr}"
            );
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Review));

        ReminderPublishGate.Release();
        (await publish.WaitAsync(Bound)).Should().BeOfType<AdvanceOutcome.Advanced>();
        (await _api.NotNull().FireTimers()).Should().NotContain(r => r.InstanceId == id);
        (await _api2.NotNull().FireTimers()).Should().NotContain(r => r.InstanceId == id);

        (await Reminder(id))
            .State.Should()
            .Be(nameof(ReminderState.Published), "the effect's target won; the timer never fired");
        (await _db.NotNull().DueAt(row)).Should().BeNull();
    }

    [Test]
    public async Task A_fire_produces_one_change_notice()
    {
        var (_, id, row) = await OpenReview();
        await _db.NotNull().MakeDue(row);
        var before = await _db.NotNull().Revision(row);

        if (store == ClusterStore.Postgres)
        {
            var listener = await SnapshotNoticeSession.Open(_cluster.NotNull().ConnectionString);
            await using var listenerDisposal = listener.ConfigureAwait(false);
            await listener.Drain();

            (await _api.NotNull().FireTimers())
                .Should()
                .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);

            (await listener.Drain())
                .Should()
                .Equal(
                    [row.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                    $"the fire is one committed write, announced by its row id only. See {Adr}"
                );
        }
        else
        {
            (await _api.NotNull().FireTimers())
                .Should()
                .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);
        }

        (await _db.NotNull().Revision(row))
            .Should()
            .Be(before + 1, "SQLite's poll sees one new revision");
    }

    [Test]
    public async Task A_timer_on_a_system_owned_instance_fires()
    {
        var instance = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Ok));
        var started = await Wait(instance.Id);
        var token = started.InvokeToken.NotNull();
        await _db.NotNull().MakeDue(started.RowId);

        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == instance.Id)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Fired);

        var row = await Wait(instance.Id);
        row.State.Should().Be(nameof(WaitState.TimedOut));
        row.InvokeToken.Should().BeNull("the timer left the invoking state");
        (await _api.NotNull().Entry(token))
            .NotNull()
            .Status.Should()
            .Be(WorkQueueStatus.Cancelled, $"leaving cancels the queued run. See {Adr}");
    }

    #endregion

    #region A5: races

    [Test]
    public async Task A_client_event_leaving_first_wins_and_cancels_the_timer()
    {
        var (user, id, row) = await OpenReview();
        await _db.NotNull().MakeDue(row);
        var before = await _db.NotNull().Revision(row);

        // The fire has read the row as due; the client's event leaves Review before the fire writes.
        var fire = await PausedFire(_api.NotNull(), id);
        (await _api2.NotNull().Send(user, id, ReminderTrigger.Approve))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var fired = await fire.Release();

        fired
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Lost, $"the fire's conditional write matches nothing. See {Adr}");
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Done));
        (await _db.NotNull().DueAt(row))
            .Should()
            .BeNull($"leaving the timed state cancels its timer. See {Adr}");
        (await _db.NotNull().Revision(row))
            .Should()
            .Be(before + 1, "only the client's event wrote");
    }

    [Test]
    public async Task A_fire_first_makes_the_client_event_a_no_transition()
    {
        var user = NewUser();
        var id = Guid.NewGuid();
        (await _pausing.NotNull().OpenReview(user, id))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var row = (await Reminder(id)).RowId;
        await _db.NotNull().MakeDue(row);
        var before = await _db.NotNull().Revision(row);

        _gate.Arm();
        var approve = _pausing.NotNull().Send(user, id, ReminderTrigger.Approve);
        await _gate.Reached.WaitAsync(Bound);
        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);
        _gate.Release();

        (await approve.WaitAsync(Bound))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>(
                $"the client's event meets the server's snapshot, in which Approve has no edge. See {Adr}"
            )
            .Which.Reason.Should()
            .Be(RefusalCode.NoTransition);
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Expired));
        (await _db.NotNull().Revision(row)).Should().Be(before + 1);
    }

    [Test]
    public async Task Extend_racing_the_fire_applies_exactly_one()
    {
        // The fire wins: Extend read Review, and finds Expired when it writes.
        var user = NewUser();
        var lost = Guid.NewGuid();
        (await _pausing.NotNull().OpenReview(user, lost))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var lostRow = (await Reminder(lost)).RowId;
        await _db.NotNull().MakeDue(lostRow);
        var lostBefore = await _db.NotNull().Revision(lostRow);

        _gate.Arm();
        var extend = _pausing.NotNull().Send(user, lost, ReminderTrigger.Extend);
        await _gate.Reached.WaitAsync(Bound);
        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == lost && r.Outcome == TimerFireResult.Fired);
        _gate.Release();

        (await extend.WaitAsync(Bound)).Should().NotBeOfType<AdvanceOutcome.Advanced>();
        (await Reminder(lost)).State.Should().Be(nameof(ReminderState.Expired));
        (await _db.NotNull().Revision(lostRow))
            .Should()
            .Be(lostBefore + 1, $"exactly one applied. See {Adr}");

        // Extend wins: the fire read the row as due, Extend re-enters Review before the fire writes, and the fire
        // is lost.
        var (_, won, wonRow) = await OpenReview(user);
        await _db.NotNull().MakeDue(wonRow);
        var wonBefore = await _db.NotNull().Revision(wonRow);

        var fire = await PausedFire(_api2.NotNull(), won);
        (await _api.NotNull().Send(user, won, ReminderTrigger.Extend))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        (await fire.Release())
            .Should()
            .ContainSingle(r => r.InstanceId == won)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Lost);

        (await Reminder(won)).State.Should().Be(nameof(ReminderState.Review));
        (await _db.NotNull().DueAt(wonRow))
            .Should()
            .BeAfter(await _db.NotNull().Now(), "Extend re-entered Review and restarted its timer");
        (await _db.NotNull().Revision(wonRow)).Should().Be(wonBefore + 1);
    }

    [Test]
    public async Task An_outcome_and_a_timer_on_a_timed_invoking_state_apply_exactly_one()
    {
        // The timer first: the run ended, but its outcome was not delivered before the fire.
        var timerFirst = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Ok));
        var timerFirstToken = (await Wait(timerFirst.Id)).InvokeToken.NotNull();
        await _worker.NotNull().DispatchAndRun(timerFirstToken);
        await _db.NotNull().MakeDue((await Wait(timerFirst.Id)).RowId);

        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r =>
                r.InstanceId == timerFirst.Id && r.Outcome == TimerFireResult.Fired
            );
        (await _api.NotNull().Deliver(timerFirstToken))
            .Should()
            .BeOfType<InvokeDelivery.NoTransition>("the timer left the state the run was for");
        (await Wait(timerFirst.Id)).State.Should().Be(nameof(WaitState.TimedOut));

        // The timer first, with the run still going: leaving clears the token and asks the run to stop.
        var going = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Ok));
        var goingToken = (await Wait(going.Id)).InvokeToken.NotNull();
        var goingRun = await _worker.NotNull().Dispatch(goingToken);
        await _db.NotNull().MakeDue((await Wait(going.Id)).RowId);

        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == going.Id && r.Outcome == TimerFireResult.Fired);
        (await Wait(going.Id)).InvokeToken.Should().BeNull();
        (await _worker.NotNull().Run(goingRun))
            .CancellationRequested.Should()
            .BeTrue(
                $"a timer leaves an invoking state through the outbox's leaving path. See {Adr}"
            );

        // The outcome first: it leaves the timed state, and the timer with it.
        var outcomeFirst = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Ok));
        var outcomeFirstRow = await Wait(outcomeFirst.Id);
        await _worker.NotNull().DispatchAndRun(outcomeFirstRow.InvokeToken.NotNull());
        await _db.NotNull().MakeDue(outcomeFirstRow.RowId);

        (await _api.NotNull().Deliver(outcomeFirstRow.InvokeToken.NotNull()))
            .Should()
            .BeOfType<InvokeDelivery.Moved>();
        (await _db.NotNull().DueAt(outcomeFirstRow.RowId)).Should().BeNull();
        (await _api.NotNull().FireTimers())
            .Should()
            .NotContain(r => r.InstanceId == outcomeFirst.Id);
        (await Wait(outcomeFirst.Id)).State.Should().Be(nameof(WaitState.Fetched));
    }

    [Test]
    public async Task An_operator_cancel_and_a_timer_apply_exactly_one()
    {
        // The cancel first.
        var cancelled = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Ok));
        var cancelledRow = (await Wait(cancelled.Id)).RowId;
        await _db.NotNull().MakeDue(cancelledRow);

        (await Cancel(cancelled.Id)).Outcome.Should().Be(MachineInstanceCancelOutcome.Moved);
        (await _api.NotNull().FireTimers()).Should().NotContain(r => r.InstanceId == cancelled.Id);
        (await Wait(cancelled.Id)).State.Should().Be(nameof(WaitState.Cancelled));
        (await _db.NotNull().DueAt(cancelledRow))
            .Should()
            .BeNull($"the cancel left the timed state. See {Adr}");

        // The timer first.
        var timedOut = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Ok));
        await _db.NotNull().MakeDue((await Wait(timedOut.Id)).RowId);

        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == timedOut.Id && r.Outcome == TimerFireResult.Fired);
        (await Cancel(timedOut.Id))
            .Outcome.Should()
            .Be(MachineInstanceCancelOutcome.NoLiveRun, "the timer left the state and its run");
        (await Wait(timedOut.Id)).State.Should().Be(nameof(WaitState.TimedOut));
    }

    [Test]
    public async Task Deleting_a_draft_with_a_due_timer_never_resurrects_it()
    {
        var (user, id, row) = await OpenReview();
        await _db.NotNull().MakeDue(row);

        await _api.NotNull().Delete(user, id);
        (await _api.NotNull().FireTimers()).Should().NotContain(r => r.InstanceId == id);
        (await _api2.NotNull().FireTimers()).Should().NotContain(r => r.InstanceId == id);

        (await _api.NotNull().Rows(id, ReminderMachine.MachineId))
            .Should()
            .BeEmpty($"a fire is an update, never an insert. See {Adr}");

        (await _api.NotNull().OpenReview(user, id)).Should().BeOfType<AdvanceOutcome.Advanced>();
        (await Reminder(id))
            .RowId.Should()
            .NotBe(row, "a re-created draft is a new incarnation under a new row id");
    }

    [Test]
    public async Task Expiry_racing_a_fire_ends_deleted_or_expired_never_both()
    {
        var expiring = _cluster
            .NotNull()
            .Host(
                machines: true,
                scheduler: false,
                TimerHosts.Publishing,
                options: o => o.MaxDraftLifetime = TimeSpan.FromHours(1)
            );
        await using var expiringDisposal = expiring.ConfigureAwait(false);
        var sweeper = expiring.Services.GetRequiredService<DraftSweeper>();

        // The sweep deletes first: the fire read the row as due, and finds it gone when it writes.
        var (_, deleted, deletedRow) = await OpenReview();
        await _db.NotNull().MakeDue(deletedRow);
        await _api.NotNull().AgeDraft(deletedRow, TimeSpan.FromHours(2));
        var fire = await PausedFire(_api2.NotNull(), deleted);
        (await sweeper.SweepOnce()).Should().Be(1, "the draft is past its lifetime");
        (await fire.Release())
            .Should()
            .ContainSingle(r => r.InstanceId == deleted)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Lost, $"a fire is an update, never an insert. See {Adr}");
        (await _api.NotNull().Rows(deleted, ReminderMachine.MachineId)).Should().BeEmpty();

        // The fire writes first: the sweep read the row as expired, and its delete, conditional on the revision it
        // read, matches nothing.
        var (_, fired, firedRow) = await OpenReview();
        await _db.NotNull().MakeDue(firedRow);
        await _api.NotNull().AgeDraft(firedRow, TimeSpan.FromHours(2));
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sweep = sweeper.SweepOnce(afterRead: async ids =>
        {
            if (!ids.Contains(fired))
                return;
            read.TrySetResult();
            await resume.Task.ConfigureAwait(false);
        });
        await read.Task.WaitAsync(Bound);
        (await _api2.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == fired && r.Outcome == TimerFireResult.Fired);
        resume.TrySetResult();
        (await sweep.WaitAsync(Bound))
            .Should()
            .Be(0, "the row was written after the sweep read it");

        (await _api.NotNull().Rows(fired, ReminderMachine.MachineId))
            .Should()
            .ContainSingle()
            .Which.State.Should()
            .Be(
                nameof(ReminderState.Expired),
                $"a row that survived the expiry is one the timer fired. See {Adr}"
            );
    }

    [Test]
    public async Task A_request_id_retry_after_the_timer_fired_replays_from_the_ring()
    {
        var (user, id, row) = await OpenReview();
        (await _api.NotNull().Send(user, id, ReminderTrigger.Extend, requestId: "extend-1"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var extended = await _db.NotNull().Revision(row);
        // Backdating due_at is a write too, which the database numbers.
        await _db.NotNull().MakeDue(row);
        var due = await _db.NotNull().Revision(row);
        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);
        (await _db.NotNull().Revision(row)).Should().Be(due + 1);

        var retry = await _api.NotNull()
            .Send(user, id, ReminderTrigger.Extend, requestId: "extend-1");

        var replayed = retry
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>(
                $"the server's own write does not evict the client's request from the ring. See {Adr}"
            )
            .Subject;
        replayed.Snapshot.State.Should().Be(nameof(ReminderState.Review), "the recorded result");
        replayed.Revision.Should().Be(extended);
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Expired), "nothing is written");
        (await _db.NotNull().Revision(row)).Should().Be(due + 1);
    }

    #endregion

    #region A6: a timer leaving an invoking state

    [Test]
    public async Task A_timer_leaving_an_invoking_state_cancels_its_run_on_another_host()
    {
        var instance = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Gate));
        var started = await Wait(instance.Id);
        var token = started.InvokeToken.NotNull();
        var runId = await _worker.NotNull().Dispatch(token);
        var job = _worker.NotNull().RunJob(runId);
        await InvokedStepGate.Entered.WaitAsync(Bound);
        await _db.NotNull().MakeDue(started.RowId);

        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == instance.Id && r.Outcome == TimerFireResult.Fired);
        (await _worker.NotNull().Run(runId)).CancellationRequested.Should().BeTrue();
        InvokedStepGate.Release();
        await job.ContinueWith(_ => { }, TaskScheduler.Default).WaitAsync(Bound);

        (await _worker.NotNull().Run(runId))
            .TrainState.Should()
            .Be(
                TrainState.Cancelled,
                $"the run reads its cancel flag at its next junction, on its own host. See {Adr}"
            );
        (await _api.NotNull().Sweep()).Should().BeEmpty();
        (await Wait(instance.Id)).State.Should().Be(nameof(WaitState.TimedOut));
    }

    [Test]
    public async Task The_late_outcome_after_a_timer_left_is_a_no_transition()
    {
        var instance = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Ok));
        var started = await Wait(instance.Id);
        await _worker.NotNull().DispatchAndRun(started.InvokeToken.NotNull());
        await _db.NotNull().MakeDue(started.RowId);

        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == instance.Id && r.Outcome == TimerFireResult.Fired);
        (await _api.NotNull().Deliver(started.InvokeToken.NotNull()))
            .Should()
            .BeOfType<InvokeDelivery.NoTransition>(
                $"the outcome reaches only the entry that queued it, which the timer ended. See {Adr}"
            );

        var row = await Wait(instance.Id);
        row.State.Should().Be(nameof(WaitState.TimedOut));
        JsonNode
            .Parse(row.Context)
            .NotNull()["artifact"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .BeEmpty();
    }

    [Test]
    public async Task The_run_timeout_and_the_state_timer_apply_one()
    {
        var instance = await _api.NotNull().StartWait(WaitMachine.Context(InvokedStepModes.Ok));
        var started = await Wait(instance.Id);
        var runId = await _full.NotNull().Dispatch(started.InvokeToken.NotNull());
        await _full.NotNull().Age(runId, TimeSpan.FromHours(1));
        await _db.NotNull().MakeDue(started.RowId);
        var before = await _db.NotNull().Revision(started.RowId);

        await Task.WhenAll(_full.NotNull().RunManifestManager(), _api2.NotNull().FireTimers())
            .WaitAsync(Bound);

        (await Wait(instance.Id))
            .State.Should()
            .BeOneOf(
                [nameof(WaitState.Failed), nameof(WaitState.TimedOut)],
                $"the reaped run's failure and the timer each leave Fetching, and only one can. See {Adr}"
            );
        (await _db.NotNull().Revision(started.RowId)).Should().Be(before + 1, "one of them wrote");
        (await _db.NotNull().DueAt(started.RowId)).Should().BeNull();
    }

    [Test]
    public async Task A_stranded_invoking_state_leaves_by_its_timer()
    {
        var instance = await _api.NotNull()
            .StartSystem<StrandingWaitMachine>(WaitMachine.Context(InvokedStepModes.Fail));
        var started = (
            await _api.NotNull().Row(instance.Id, StrandingWaitMachine.MachineId)
        ).NotNull();
        await _full.NotNull().DispatchAndRun(started.InvokeToken.NotNull());
        var stranded = (
            await _api.NotNull().Row(instance.Id, StrandingWaitMachine.MachineId)
        ).NotNull();
        stranded.State.Should().Be(nameof(WaitState.Fetching));
        stranded.InvokeStrandedState.Should().NotBeNull("the failure's target could not be queued");
        await _db.NotNull().MakeDue(stranded.RowId);

        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == instance.Id && r.Outcome == TimerFireResult.Fired);
        (await _api.NotNull().Row(instance.Id, StrandingWaitMachine.MachineId))
            .NotNull()
            .State.Should()
            .Be(
                nameof(WaitState.TimedOut),
                $"a strand keeps its timer, which still leaves the state. See {Adr}"
            );
    }

    #endregion

    // Starts a sweep on host and holds it after it has read the due row of id, before the row's transaction begins.
    private static async Task<PausedSweep> PausedFire(ClusterHost host, Guid id)
    {
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Reconciler.AfterRead = async row =>
        {
            if (row.Id != id)
                return;
            read.TrySetResult();
            await resume.Task.ConfigureAwait(false);
        };
        var sweep = host.FireTimers();
        await read.Task.WaitAsync(Bound).ConfigureAwait(false);
        return new PausedSweep(sweep, resume);
    }

    private sealed record PausedSweep(
        Task<IReadOnlyList<TimerFireResult>> Sweep,
        TaskCompletionSource Resume
    )
    {
        public async Task<IReadOnlyList<TimerFireResult>> Release()
        {
            Resume.TrySetResult();
            return await Sweep.WaitAsync(Bound).ConfigureAwait(false);
        }
    }

    private async Task<(string User, Guid Id, long Row)> OpenReview(string? user = null)
    {
        user ??= NewUser();
        var id = Guid.NewGuid();
        (await _api.NotNull().OpenReview(user, id).ConfigureAwait(false))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        return (user, id, (await Reminder(id).ConfigureAwait(false)).RowId);
    }

    private async Task<SnapshotDraft> Reminder(Guid id) =>
        (await _api.NotNull().Row(id, ReminderMachine.MachineId).ConfigureAwait(false)).NotNull();

    private async Task<SnapshotDraft> Wait(Guid id) =>
        (await _api.NotNull().Row(id, WaitMachine.MachineId).ConfigureAwait(false)).NotNull();

    private async Task<MachineInstanceCancelResult> Cancel(Guid id)
    {
        var scope = _full.NotNull().Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IOperationsService>()
            .CancelMachineInstanceAsync(
                new MachineInstanceKey(WaitMachine.MachineId, SnapshotOwnerKind.System, id),
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    private static string NewUser() => "user-" + Guid.NewGuid().ToString("N");

    /// <summary>Counts the failed fires the state-machine meter reports while it is alive.</summary>
    private sealed class FailedFires : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _count;

        public FailedFires()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (
                    string.Equals(
                        instrument.Meter.Name,
                        StateMachineInstruments.MeterName,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        instrument.Name,
                        StateMachineInstruments.TimerFireFailed,
                        StringComparison.Ordinal
                    )
                )
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, value, _, _) => Interlocked.Add(ref _count, value)
            );
            _listener.SetMeasurementEventCallback<int>(
                (_, value, _, _) => Interlocked.Add(ref _count, value)
            );
            _listener.Start();
        }

        public long Count => Interlocked.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
