using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Every answer that carries a snapshot carries the due time its row was left with, beside it: a start, an advance
/// (plain, and through the invoke outbox), an effect's send, a host's advance, a load, and the hub's re-read of a
/// row, which is what a push is built from. Null once the row is in a state with no timer. The value is the row's, as
/// the sweep reads it, so a client counts down to the moment the server fires.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class TimerDueAtAnswerTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "due-answer-user";

    private static readonly TimeSpan Within = TimeSpan.FromSeconds(15);

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = InstanceHost.Create(provider, launcher: new RecordingLauncher());
        _host.Services.GetRequiredService<SnapshotChangeListener>().PollInterval =
            TimeSpan.FromMilliseconds(50);
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private async Task<DateTimeOffset?> RowDueAt(Guid id) =>
        (
            await RawSql.Timer(_host, id)
            ?? throw new InvalidOperationException($"No row holds the id {id}.")
        ).DueAt;

    private static void ShouldBeTheRows(DateTimeOffset? answer, DateTimeOffset? row, string path)
    {
        row.Should().NotBeNull($"{path} enters a timed state");
        answer.Should().NotBeNull($"{path} answers the due time beside the snapshot. See {Adr}");
        answer!
            .Value.Should()
            .BeCloseTo(
                row!.Value,
                TimeSpan.FromMilliseconds(1),
                $"{path} answers the row's due time"
            );
    }

    [Test]
    public async Task A_start_into_a_timed_state_answers_its_due_time()
    {
        StartOutcome.Started started;
        using (var scope = _host.Scope())
            started = (
                await _host
                    .Service(scope, ExpiringMachine.Id)
                    .Start(new Actor.Client(User), null, "start-due")
            )
                .Should()
                .BeOfType<StartOutcome.Started>()
                .Subject;

        ShouldBeTheRows(started.DueAt, await RowDueAt(started.Id), "start");

        // A retry answers with the instance the first start created, and its due time.
        using (var scope = _host.Scope())
            (
                await _host
                    .Service(scope, ExpiringMachine.Id)
                    .Start(new Actor.Client(User), null, "start-due")
            )
                .Should()
                .BeOfType<StartOutcome.Started>()
                .Which.DueAt.Should()
                .Be(started.DueAt);
    }

    [Test]
    public async Task An_advance_answers_the_due_time_it_armed_and_null_when_it_leaves()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, ReminderMachine.Json("Idle"));

        AdvanceOutcome.Advanced opened;
        using (var scope = _host.Scope())
            opened = (
                await _host
                    .Service(scope, ReminderMachine.Id)
                    .Advance(new Actor.Client(User, "Editor"), id, "Open")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Subject;
        ShouldBeTheRows(opened.DueAt, await RowDueAt(id), "an advance");

        using (var scope = _host.Scope())
        {
            var loaded = (await _host.Service(scope, ReminderMachine.Id).Load(User, id))
                .Should()
                .BeOfType<LoadResult.Loaded>()
                .Subject;
            ShouldBeTheRows(loaded.DueAt, await RowDueAt(id), "a load");
        }

        using (var scope = _host.Scope())
            (
                await _host
                    .Service(scope, ReminderMachine.Id)
                    .Advance(
                        User,
                        id,
                        "Approve",
                        new System.Text.Json.Nodes.JsonObject { ["verdict"] = "approved" }
                    )
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Which.DueAt.Should()
                .BeNull("Done has no timer");
    }

    [Test]
    public async Task An_advance_into_a_timed_invoking_state_answers_its_due_time()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, WaitMachine.Json("Idle", TimeSpan.FromMinutes(5)));

        AdvanceOutcome.Advanced fetching;
        using (var scope = _host.Scope())
            fetching = (await _host.Service(scope, WaitMachine.Id).Advance(User, id, "Fetch"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Subject;

        ShouldBeTheRows(fetching.DueAt, await RowDueAt(id), "an advance through the invoke outbox");
    }

    [Test]
    public async Task A_send_answers_the_due_time_of_the_state_its_effect_entered()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, RenewalMachine.DraftJson);

        AdvanceOutcome.Advanced sent;
        using (var scope = _host.Scope())
            sent = (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .EffectRunner(RenewalMachine.Id)!
                    .Run(User, id, "charge-due")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Subject;
        ShouldBeTheRows(sent.DueAt, await RowDueAt(id), "a send");

        // A retry of the send replays the stored result, with its due time: the row is still at the revision the
        // send recorded, so the replay knows it.
        AdvanceOutcome.Advanced replayed;
        using (var scope = _host.Scope())
            replayed = (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .EffectRunner(RenewalMachine.Id)!
                    .Run(User, id, "charge-due")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Subject;
        replayed.DueAt.Should().Be(sent.DueAt);
        replayed.DueAtKnown.Should().BeTrue($"the row is at the recorded revision. See {Adr}");
    }

    [Test]
    public async Task A_hosts_advance_answers_its_due_time()
    {
        var key = MachineKey.Of("timers", Guid.NewGuid().ToString());
        var instance = await _host.Start<SystemReminderMachine>(key);

        AdvanceOutcome.Advanced opened;
        using (var scope = _host.Scope())
            opened = (
                await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Advance<SystemReminderMachine>(key, "Open")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Subject;

        ShouldBeTheRows(opened.DueAt, await RowDueAt(instance.Id), "a host's advance");
    }

    [Test]
    public async Task The_hubs_reread_carries_the_rows_due_time()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, ReminderMachine.Json("Idle"));
        var hub = _host.Services.GetRequiredService<SnapshotWatchHub>();
        await using var watch = hub.Watch(await _host.RowId(id));

        var first = await Next(watch);
        first.DueAt.Should().BeNull("Idle has no timer");

        using (var scope = _host.Scope())
            (
                await _host
                    .Service(scope, ReminderMachine.Id)
                    .Advance(new Actor.Client(User, "Editor"), id, "Open")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        var opened = await Next(watch);
        opened.State.Should().Be("Review");
        ShouldBeTheRows(opened.DueAt, await RowDueAt(id), "the hub's re-read");
    }

    private static async Task<SnapshotRowChange> Next(SnapshotWatch watch)
    {
        using var timeout = new CancellationTokenSource(Within);
        return await watch.Changes.ReadAsync(timeout.Token);
    }
}
