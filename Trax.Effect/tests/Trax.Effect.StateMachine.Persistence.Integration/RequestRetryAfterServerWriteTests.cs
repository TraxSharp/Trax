using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A retried request whose id the ring holds with the same trigger and input replays the result recorded for it,
/// wherever the instance is now. A timer or an outcome that brings the instance back to the state the request fired
/// from does not make the retry a new request: the client sent one request, and it is applied once.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class RequestRetryAfterServerWriteTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "retry-user";

    private InstanceHost _host = null!;
    private RecordingLauncher _launcher = null!;
    private SharedCharge _charge = null!;

    [SetUp]
    public void SetUp()
    {
        _launcher = new RecordingLauncher();
        _charge = new SharedCharge();
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

    private async Task<AdvanceOutcome> Advance(
        string machine,
        Guid id,
        string trigger,
        string requestId
    )
    {
        using var scope = _host.Scope();
        return await _host.Service(scope, machine).Advance(User, id, trigger, null, requestId);
    }

    private async Task FireDueTimer(Guid id)
    {
        await RawSql.SetDueAt(_host, id, (await RawSql.Now(_host)).AddMinutes(-1));
        (await Reconciler.FireDueTimersOnce(CancellationToken.None))
            .Should()
            .ContainSingle(r => r.InstanceId == id)
            .Which.Outcome.Should()
            .Be(TimerFireResult.Fired);
    }

    [Test]
    public async Task A_retry_after_a_timer_brought_the_instance_back_replays_its_recorded_result()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, PlainTimerMachine.Json("Idle"));
        var first = (await Advance(PlainTimerMachine.Id, id, "Wait", "r1"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Subject;
        first.Snapshot.State.Should().Be("Waiting");

        await FireDueTimer(id);
        var afterTimer = (await RawSql.Timer(_host, id))!;
        afterTimer.State.Should().Be("Idle", "the timer went back to the state r1 fired from");

        var retry = (await Advance(PlainTimerMachine.Id, id, "Wait", "r1"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Subject;

        retry
            .Snapshot.State.Should()
            .Be("Waiting", $"the retry replays r1's recorded result. See {Adr}");
        retry.Revision.Should().Be(first.Revision);
        first.DueAtKnown.Should().BeTrue("the advance itself knows the due time it armed");
        retry
            .DueAtKnown.Should()
            .BeFalse(
                $"the row has moved past the recorded revision, so the replay cannot know Waiting's due time. See {Adr}"
            );
        retry.DueAt.Should().BeNull("an unknown due time is left out, never guessed");
        var row = (await RawSql.Timer(_host, id))!;
        row.State.Should().Be("Idle", "a replay writes nothing");
        row.Revision.Should().Be(afterTimer.Revision);
    }

    [Test]
    public async Task A_retried_send_after_a_timer_moved_the_instance_on_replays_its_recorded_result()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, RenewalMachine.DraftJson);
        AdvanceOutcome.Advanced first;
        using (var scope = _host.Scope())
            first = (
                await _host.Service(scope, RenewalMachine.Id).Send(new Actor.Client(User), id, "c1")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Subject;
        first.Snapshot.State.Should().Be(nameof(RenewalState.Active));
        first.DueAt.Should().NotBeNull("Active arms its timer");

        await FireDueTimer(id);
        var afterTimer = (await RawSql.Timer(_host, id))!;
        afterTimer.State.Should().Be(nameof(RenewalState.Lapsed));

        AdvanceOutcome retry;
        using (var scope = _host.Scope())
            retry = await _host
                .Service(scope, RenewalMachine.Id)
                .Send(new Actor.Client(User), id, "c1");

        var replayed = retry
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>(
                $"a retried send replays its recorded result wherever the instance is now. See {Adr}"
            )
            .Subject;
        replayed.Snapshot.State.Should().Be(nameof(RenewalState.Active));
        replayed.Revision.Should().Be(first.Revision);
        replayed
            .DueAt.Should()
            .BeNull(
                "the row has moved on from the recorded revision, so its due time is not the answer's"
            );
        first.DueAtKnown.Should().BeTrue("the send that armed the timer knows its due time");
        replayed
            .DueAtKnown.Should()
            .BeFalse(
                $"a null due time on a stale replay means unknown, not cleared, and the answer says so. See {Adr}"
            );

        SendSnapshotOutput generic;
        using (var scope = _host.Scope())
            generic = await new SendSnapshotJunction(
                scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>(),
                new FakePrincipal(User),
                services: scope.ServiceProvider
            ).Run(
                new SendSnapshotInput
                {
                    Machine = RenewalMachine.Id,
                    Id = id,
                    RequestId = "c1",
                }
            );
        generic.Problem.Should().BeNull();
        generic.DueAt.Should().BeNull();
        generic
            .DueAtKnown.Should()
            .BeFalse($"the generic send says the same of the same replay. See {Adr}");
        _charge.Calls.Should().Be(1, "one send runs the effect once");
        var row = (await RawSql.Timer(_host, id))!;
        row.State.Should().Be(nameof(RenewalState.Lapsed), "a replay writes nothing");
        row.Revision.Should().Be(afterTimer.Revision);
    }

    [Test]
    public async Task A_retry_after_a_failed_run_and_its_cooldown_queues_no_second_run()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, WaitMachine.Json("Idle", TimeSpan.FromMinutes(5)));
        var first = (await Advance(WaitMachine.Id, id, "Fetch", "fetch-1"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Subject;
        first.Snapshot.State.Should().Be("Fetching");
        _launcher.Launches.Should().ContainSingle();

        // The run fails (its records are gone), its outcome moves the instance into Cooldown, and Cooldown's timer
        // brings it back to Idle, where fetch-1 fired from.
        var token = (await _host.Rows(id)).Single().InvokeToken!;
        using (var scope = _host.Scope())
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .WorkQueues.Where(w => w.ExternalId == token)
                .ExecuteDeleteAsync();
        (await _host.Services.GetRequiredService<IInvokedRunOutcomes>().Deliver(token))
            .Should()
            .Be("Cooldown");
        await FireDueTimer(id);
        var before = (await RawSql.Timer(_host, id))!;
        before.State.Should().Be("Idle");

        var retry = (await Advance(WaitMachine.Id, id, "Fetch", "fetch-1"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Subject;

        retry.Revision.Should().Be(first.Revision, $"the retry replays fetch-1. See {Adr}");
        _launcher
            .Launches.Should()
            .ContainSingle("one request queues one run, however often it is retried");
        var row = (await RawSql.Timer(_host, id))!;
        row.State.Should().Be("Idle");
        row.Revision.Should().Be(before.Revision);
    }
}
