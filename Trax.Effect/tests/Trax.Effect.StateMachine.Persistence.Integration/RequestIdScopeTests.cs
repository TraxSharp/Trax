using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A request id replays only the request it recorded: the same id and trigger, while the draft still shows
/// that request's outcome. Anything else fires, or is refused as <c>REQUEST_ID_REUSED</c>.
///
/// <para>Enforces <c>docs/adr/0013-a-request-id-replays-only-the-request-it-recorded.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0013-a-request-id-replays-only-the-request-it-recorded.md")]
public class RequestIdScopeTests
{
    private const string Adr = "docs/adr/0013-a-request-id-replays-only-the-request-it-recorded.md";

    private static readonly ISnapshotPrincipal User = new FakePrincipal("u");

    private static SnapshotDraftService<TurnstileState, TurnstileTrigger> Service() =>
        TestTurnstile.Service(TestDb.NewStore());

    private static ISnapshotMachineRegistry NewRegistry()
    {
        var context = TestDb.NewContext();
        return new SnapshotMachineRegistry(
            new IMachine[] { new LogMachine() },
            TestDb.NewStore(context),
            TestDb.NewClaims(context),
            new IdempotentEffect(TestDb.NewClaims(context)),
            new ServiceCollection().BuildServiceProvider()
        );
    }

    private static async Task<Guid> Saved(string machine, string state, string context)
    {
        var id = Guid.NewGuid();
        (
            await new SaveSnapshotJunction(NewRegistry(), User).Run(
                new SaveSnapshotInput
                {
                    Machine = machine,
                    Id = id,
                    Snapshot =
                        $"{{\"machine\":\"{machine}\",\"version\":1,\"state\":\"{state}\",\"context\":{context}}}",
                }
            )
        ).Problem.Should().BeNull();
        return id;
    }

    private static Task<AdvanceSnapshotOutput> Advance(
        string machine,
        Guid id,
        string trigger,
        string? input,
        string? requestId = null
    ) =>
        new AdvanceSnapshotJunction(NewRegistry(), User).Run(
            new AdvanceSnapshotInput
            {
                Machine = machine,
                Id = id,
                Trigger = trigger,
                Input = input,
                RequestId = requestId,
            }
        );

    private static async Task<string> Load(string machine, Guid id) =>
        (
            await new LoadSnapshotJunction(NewRegistry(), User).Run(
                new LoadSnapshotInput { Machine = machine, Id = id }
            )
        ).Snapshot!;

    [Test]
    public async Task A_requestId_reused_for_a_different_trigger_is_refused_and_writes_nothing()
    {
        var id = Guid.NewGuid();
        await Service().Autosave("u", id, TestTurnstile.InitialJson);
        await Service()
            .Advance("u", id, "Coin", new JsonObject { ["coin"] = "quarter" }, requestId: "r1");

        // Replaying the Coin outcome as the answer to a Push would tell the client its Push happened.
        (await Service().Advance("u", id, "Push", requestId: "r1"))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(
                RefusalCode.RequestIdReused,
                $"an id replays only for the trigger it recorded ({Adr})"
            );

        (await Service().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");
    }

    [Test]
    public async Task A_request_whose_outcome_was_undone_replays_its_recorded_result()
    {
        var id = Guid.NewGuid();
        await Service().Autosave("u", id, TestTurnstile.InitialJson);
        var first = await Service()
            .Advance("u", id, "Coin", new JsonObject { ["coin"] = "quarter" }, requestId: "r1");

        // A soft save puts the draft back where r1 started. The client sent r1 once, so its retry is answered with
        // the result recorded for it and fires nothing; a new action takes a new id.
        (await Service().Autosave("u", id, TestTurnstile.InitialJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        var retry = (
            await Service()
                .Advance("u", id, "Coin", new JsonObject { ["coin"] = "quarter" }, requestId: "r1")
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Subject;
        retry.Snapshot.State.Should().Be("Unlocked", $"the recorded result replays ({Adr})");
        retry
            .Revision.Should()
            .Be(first.Should().BeOfType<AdvanceOutcome.Advanced>().Subject.Revision);
        (await Service().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Locked", $"a replay writes nothing ({Adr})");
    }

    [Test]
    public async Task A_request_recorded_without_its_trigger_is_refused_rather_than_replayed()
    {
        // A row written before the trigger was recorded (or by a store that records the id alone).
        var id = Guid.NewGuid();
        await Service().Autosave("u", id, TestTurnstile.InitialJson);
        var stored = await TestDb.NewStore().Get("u", id);
        await TestDb
            .NewStore()
            .Update(
                "u",
                id,
                TestTurnstile.Machine.Definition.CreateInitialSnapshot(),
                stored!.Token,
                "r1"
            );

        (await Service().Advance("u", id, "Coin", new JsonObject { ["coin"] = "quarter" }, "r1"))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(
                RefusalCode.RequestIdReused,
                $"a request recorded without its trigger never replays ({Adr})"
            );
    }

    [Test]
    public async Task A_retried_advance_on_a_self_loop_replays_instead_of_firing_again()
    {
        var id = await Saved("log", "Open", "{}");
        (await Advance("log", id, "Append", "{\"text\":\"a\"}", requestId: "r1"))
            .Problem.Should()
            .BeNull();

        // The draft is still in the state r1 fired from, but the edge loops back to it, so that is where a
        // completed r1 leaves it: this is a retry, and appending again would double the text.
        var retry = await Advance("log", id, "Append", "{\"text\":\"a\"}", requestId: "r1");

        retry.Problem.Should().BeNull();
        (await Load("log", id))
            .Should()
            .Contain("\"text\":\"a\"", $"a retry on a self-loop replays ({Adr})");
    }

    private const string Ring =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static ISnapshotDraftService Log() =>
        new LogMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims());

    private static Snapshot LogSnapshot(string text) =>
        new()
        {
            Machine = "log",
            Version = 1,
            State = "Open",
            Context = new JsonObject { ["text"] = text },
        };

    private static async Task<(string User, Guid Id)> NewLog()
    {
        var user = $"u-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert(user, id, LogSnapshot(""))).Should().BeTrue();
        return (user, id);
    }

    private static async Task<string> Append(string user, Guid id, string text, string requestId) =>
        (await Log().Advance(user, id, "Append", new JsonObject { ["text"] = text }, requestId))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.Context["text"]!.GetValue<string>();

    private static async Task<string> StoredText(string user, Guid id) =>
        ((LoadResult.Loaded)await Log().Load(user, id)).Snapshot.Context[
            "text"
        ]!.GetValue<string>();

    [Test]
    [Property("adr", Ring)]
    public async Task A_retry_of_any_of_the_last_sixteen_requests_replays_its_recorded_result()
    {
        var (user, id) = await NewLog();
        var first = await Append(user, id, "1,", "r1");
        for (var i = 2; i <= RequestIdRing.Capacity; i++)
            await Append(user, id, $"{i},", $"r{i}");
        var stored = await StoredText(user, id);

        // r1 is the oldest of the last sixteen: its retry is answered with what r1 was answered with then.
        (await Append(user, id, "1,", "r1"))
            .Should()
            .Be(
                first,
                $"a retry replays the result recorded for it, not the current snapshot. See {Ring}"
            );
        (await StoredText(user, id)).Should().Be(stored, "a replay fires nothing");
        (await TestDb.NewStore().Get(user, id))!
            .RecentRequests.Should()
            .HaveCount(RequestIdRing.Capacity)
            .And.Contain(r => r.RequestId == "r1");
    }

    [Test]
    [Property("adr", Ring)]
    public async Task A_server_write_never_evicts_a_client_request_from_the_ring()
    {
        var (user, id) = await NewLog();
        var first = await Append(user, id, "a", "r1");

        // More server writes than the ring holds: an invoked run's token set, then its outcome applied.
        var store = (IMachineInstanceStore)TestDb.NewStore();
        for (var i = 0; i < RequestIdRing.Capacity + 4; i++)
        {
            var row = await store.Get(DraftOwner.User(user), "log", id);
            var token = $"run-{Guid.NewGuid():N}";
            (
                await store.SetInvokeToken(
                    DraftOwner.User(user),
                    "log",
                    id,
                    row!.Token,
                    new InvokeTokenWrite(token)
                )
            )
                .Should()
                .BeTrue();
            (await store.ApplyByInvokeToken(token, LogSnapshot(first), nextInvokeToken: null))
                .Should()
                .BeTrue();
        }
        for (var i = 2; i < RequestIdRing.Capacity; i++)
            await Append(user, id, "", $"r{i}");
        var stored = await StoredText(user, id);

        (await Append(user, id, "a", "r1"))
            .Should()
            .Be(first, $"only client requests take a place in the ring. See {Ring}");
        (await StoredText(user, id)).Should().Be(stored);
    }

    [Test]
    [Property("adr", Ring)]
    public async Task A_request_evicted_from_the_ring_is_answered_as_the_ADR_says()
    {
        // An evicted id is new again: it fires under every rule, as any request does.
        var (user, id) = await NewLog();
        for (var i = 1; i <= RequestIdRing.Capacity + 1; i++)
            await Append(user, id, "x", $"r{i}");
        (await TestDb.NewStore().Get(user, id))!
            .RecentRequests.Should()
            .NotBeNull()
            .And.HaveCount(RequestIdRing.Capacity)
            .And.NotContain(r => r.RequestId == "r1", "the oldest request was evicted");

        (await Append(user, id, "x", "r1"))
            .Should()
            .Be(
                new string('x', RequestIdRing.Capacity + 2),
                $"an evicted id is a new request and fires again. See {Ring}"
            );

        // From a state the machine has left, the evicted request finds no edge.
        var turnstile = Guid.NewGuid();
        await Service().Autosave(user, turnstile, TestTurnstile.InitialJson);
        for (var i = 1; i <= RequestIdRing.Capacity + 1; i++)
            await Service()
                .Advance(
                    user,
                    turnstile,
                    i % 2 == 1 ? "Coin" : "Push",
                    i % 2 == 1 ? new JsonObject { ["coin"] = "quarter" } : null,
                    $"t{i}"
                );
        (
            await Service()
                .Advance(user, turnstile, "Coin", new JsonObject { ["coin"] = "quarter" }, "t1")
        )
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.NoTransition, "the turnstile is already unlocked");
    }

    [Test]
    [Property("adr", Ring)]
    public async Task One_requestId_with_a_different_trigger_or_input_is_request_id_reused()
    {
        var user = $"u-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        await Service().Autosave(user, id, TestTurnstile.InitialJson);
        await Service().Advance(user, id, "Coin", new JsonObject { ["coin"] = "quarter" }, "r1");
        await Service().Advance(user, id, "Push", requestId: "r2");

        (await Service().Advance(user, id, "Push", requestId: "r1"))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.RequestIdReused, $"r1 named a Coin, not a Push. See {Ring}");
        (await Service().Advance(user, id, "Coin", new JsonObject { ["coin"] = "dollar" }, "r1"))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.RequestIdReused, $"r1 paid a quarter, not a dollar. See {Ring}");
        (await Service().Load(user, id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Locked");
    }

    [Test]
    [Property("adr", Ring)]
    [CancelAfter(60_000)]
    public async Task Concurrent_first_attempts_of_one_request_fire_once()
    {
        var (user, id) = await NewLog();
        await Append(user, id, "a", "r1");

        // Ten attempts of r2, none of which has been recorded yet, released together so they race the first
        // write: one fires, and every other one loses that race and is answered with what r2 was answered with.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable
            .Range(0, 10)
            .Select(_ =>
                Task.Run(async () =>
                {
                    await release.Task;
                    return await Log()
                        .Advance(user, id, "Append", new JsonObject { ["text"] = "b" }, "r2");
                })
            )
            .ToList();
        release.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        outcomes
            .Should()
            .AllSatisfy(o =>
                o.Should().BeOfType<AdvanceOutcome.Advanced>().Which.Snapshot.Context["text"]!
                    .GetValue<string>()
                    .Should()
                    .Be("ab", $"every attempt of r2 is answered with r2's one result. See {Ring}")
            );
        (await StoredText(user, id))
            .Should()
            .Be("ab", $"r2 fires once however many attempts race it. See {Ring}");
    }

    [Test]
    [Property("adr", Ring)]
    [CancelAfter(60_000)]
    public async Task Concurrent_retries_of_a_recorded_request_replay_and_fire_nothing()
    {
        var (user, id) = await NewLog();
        await Append(user, id, "a", "r1");
        await Append(user, id, "b", "r2");

        var retries = await Task.WhenAll(
            Enumerable
                .Range(0, 10)
                .Select(_ =>
                    Task.Run(() =>
                        Log().Advance(user, id, "Append", new JsonObject { ["text"] = "a" }, "r1")
                    )
                )
        );

        retries.Should().AllBeOfType<AdvanceOutcome.Advanced>();
        (await StoredText(user, id))
            .Should()
            .Be("ab", $"every retry of r1 replays, none fires again. See {Ring}");
    }
}
