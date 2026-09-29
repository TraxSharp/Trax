using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A request id replays only the request it recorded: the same id and trigger, while the draft still shows
/// that request's outcome. Anything else fires, or is refused as <c>request-id-reused</c>.
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
            new EfSnapshotStore(context),
            new EfEffectClaimStore(context),
            new IdempotentEffect(new EfEffectClaimStore(context)),
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
            .Be("request-id-reused", $"an id replays only for the trigger it recorded ({Adr})");

        (await Service().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");
    }

    [Test]
    public async Task A_request_whose_outcome_was_undone_fires_again_instead_of_replaying()
    {
        var id = Guid.NewGuid();
        await Service().Autosave("u", id, TestTurnstile.InitialJson);
        await Service()
            .Advance("u", id, "Coin", new JsonObject { ["coin"] = "quarter" }, requestId: "r1");

        // A soft save puts the draft back where r1 started. Replaying would answer "Unlocked" while the
        // stored draft stays Locked; the same request is new again and fires.
        (await Service().Autosave("u", id, TestTurnstile.InitialJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        (
            await Service()
                .Advance("u", id, "Coin", new JsonObject { ["coin"] = "dollar" }, requestId: "r1")
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.Context["paidWith"]!
            .GetValue<string>()
            .Should()
            .Be("dollar");
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
                "request-id-reused",
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
}
