using AwesomeAssertions;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Tests.Integration.Fakes.Timers;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.Timers;

/// <summary>
/// A deploy that changes a machine's timers while rows wait on them: a due time is set at entry and a later
/// definition never moves it, a timer the new definition no longer declares is cleared and never fired, a host never
/// fires a row it cannot read, and a migrated snapshot whose state survives keeps its due time. Each host runs one
/// definition of <see cref="ReminderMachine"/>; all share one database. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
/// <remarks>
/// That adding a timer changes the machine's persisted document ids is a Trax.Api concern, tested there.
/// </remarks>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TimerVersionChangeTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private InvokeCluster _cluster = null!;
    private TimerDb _db = null!;

    private ClusterHost _v1 = null!;
    private ClusterHost _v2WithoutTimer = null!;
    private ClusterHost _v2LongerTimer = null!;
    private ClusterHost _v2SameTimer = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _db = new TimerDb(_cluster);
        _v1 = _cluster.Host(machines: true, scheduler: false, TimerHosts.Publishing);
        _v2WithoutTimer = Deployed(ReminderShape.V2WithoutTimer);
        _v2LongerTimer = Deployed(ReminderShape.V2LongerTimer);
        _v2SameTimer = Deployed(ReminderShape.V2SameTimer);

        ClusterHost Deployed(ReminderShape shape) =>
            _cluster.Host(
                machines: true,
                scheduler: false,
                TimerHosts.All(TimerHosts.Publishing, TimerHosts.Deploy(shape))
            );
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _v1.DisposeAsync();
        await _v2WithoutTimer.DisposeAsync();
        await _v2LongerTimer.DisposeAsync();
        await _v2SameTimer.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [SetUp]
    public Task Reset() => _cluster.Reset();

    [Test]
    public async Task A_v2_without_the_timer_clears_a_stale_due_at_and_does_not_fire()
    {
        var (_, id, row) = await OpenReview(_v1);
        await _db.MakeDue(row);

        (await _v2WithoutTimer.FireTimers())
            .Should()
            .NotContain(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);

        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Review));
        (await _db.DueAt(row))
            .Should()
            .BeNull(
                $"a due time the definition no longer declares is cleared by the sweep. See {Adr}"
            );
    }

    [Test]
    public async Task A_changed_duration_keeps_the_due_at_set_at_entry()
    {
        var (_, id, row) = await OpenReview(_v1);
        var due = await _db.DueAt(row);
        due.Should().NotBeNull("version 1 armed its ten-minute timer");

        (await _v2LongerTimer.FireTimers()).Should().NotContain(r => r.InstanceId == id);
        (await _db.DueAt(row))
            .Should()
            .Be(due, $"the duration is read at entry, never again. See {Adr}");

        await _db.MakeDue(row);
        (await _v2LongerTimer.FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Expired));
    }

    [Test]
    public async Task A_v1_host_does_not_fire_a_row_written_by_v2()
    {
        var (_, id, row) = await OpenReview(_v2SameTimer, version: 2);
        await _db.MakeDue(row);

        var fires = await _v1.FireTimers();
        fires
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(
                TimerFireResult.NotHere,
                $"the row's stored definition decides, and version 1 cannot read version 2. See {Adr}"
            );
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Review));

        (await _v2SameTimer.FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);
    }

    [Test]
    public async Task A_migrated_snapshot_keeps_its_due_at_when_the_state_survives()
    {
        var (user, id, row) = await OpenReview(_v1);
        var due = await _db.DueAt(row);
        due.Should().NotBeNull();

        (await _v2SameTimer.Load(user, id)).Should().BeOfType<LoadResult.Loaded>();
        (await _db.DueAt(row))
            .Should()
            .Be(due, $"Review survives the migration, and so does its timer's due time. See {Adr}");

        await _db.MakeDue(row);
        (await _v2SameTimer.FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Expired));
    }

    private async Task<(string User, Guid Id, long Row)> OpenReview(
        ClusterHost host,
        int version = 1
    )
    {
        var user = "user-" + Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid();
        (await host.OpenReview(user, id, version)).Should().BeOfType<AdvanceOutcome.Advanced>();
        return (user, id, (await Reminder(id)).RowId);
    }

    private async Task<SnapshotDraft> Reminder(Guid id) =>
        (await _v1.Row(id, ReminderMachine.MachineId))!;
}
