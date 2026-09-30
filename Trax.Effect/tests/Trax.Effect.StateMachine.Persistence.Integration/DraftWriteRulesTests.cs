using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

public enum ShipState
{
    Packing,
    Shipped,
}

public enum ShipTrigger
{
    Ship,
}

/// <summary>An effect whose target state is not marked committed: <c>Packing -Ship-&gt; Shipped</c>.</summary>
public sealed class ShipMachine : Machine<ShipState, ShipTrigger>
{
    protected override void Configure(IMachineBuilder<ShipState, ShipTrigger> m)
    {
        m.Id("ship").Version(1).StartsAt(ShipState.Packing, () => new JsonObject());
        m.In(ShipState.Packing)
            .On(ShipTrigger.Ship)
            .RunsOnce<IOrderCharge>()
            .Reduce((_, input) => new JsonObject { ["receipt"] = input?["receipt"]?.DeepClone() })
            .To(ShipState.Shipped);
    }
}

/// <summary>
/// Which writes the draft operations accept. A state that an effect-bound transition lands in, or that is
/// committed, is reached only by the effect runner: the advance path refuses an effect-bound trigger, and the
/// autosave path refuses to create or move a draft into such a state, or to overwrite a stored draft it cannot
/// read.
///
/// <para>Enforces <c>docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md")]
public class DraftWriteRulesTests
{
    private const string Adr =
        "only the effect runner puts a draft into a committed state or an effect's target. See "
        + "docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md";

    private static ISnapshotDraftService Orders() =>
        new OrderMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims());

    private static string Placed(string receipt) =>
        new JsonObject
        {
            ["machine"] = "order",
            ["version"] = 1,
            ["state"] = "Placed",
            ["context"] = new JsonObject { ["items"] = new JsonArray(1), ["receipt"] = receipt },
        }.ToJsonString();

    private static async Task<Guid> SeedReview()
    {
        var id = Guid.NewGuid();
        (await Orders().Autosave("u", id, OrderMachine.ReviewSnapshot(1)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        return id;
    }

    private static async Task<string?> StoredState(Guid id) =>
        await Orders().Load("u", id) is LoadResult.Loaded loaded ? loaded.Snapshot.State : null;

    [Test]
    public async Task Advance_refuses_a_trigger_bound_to_an_effect()
    {
        var id = await SeedReview();

        var outcome = await Orders()
            .Advance("u", id, "Place", new JsonObject { ["receipt"] = "r-1" });

        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("effect-bound", Adr);
        (await StoredState(id)).Should().Be("Review");
    }

    [Test]
    public async Task The_advance_mutation_refuses_a_trigger_bound_to_an_effect()
    {
        var id = await SeedReview();
        var registry = new SnapshotMachineRegistry(
            new IMachine[] { new OrderMachine() },
            TestDb.NewStore(),
            TestDb.NewClaims(),
            new IdempotentEffect(TestDb.NewClaims()),
            new ServiceCollection()
                .AddSingleton<IOrderCharge>(new CountingEffect())
                .BuildServiceProvider()
        );

        var output = await new AdvanceSnapshotJunction(registry, new FakePrincipal("u")).Run(
            new AdvanceSnapshotInput
            {
                Machine = "order",
                Id = id,
                Trigger = "Place",
                Input = "{\"receipt\":\"r-1\"}",
            }
        );

        output.Problem!.Code.Should().Be("effect-bound", Adr);
        (await StoredState(id)).Should().Be("Review");
    }

    [Test]
    public async Task The_effect_runner_still_fires_the_bound_trigger()
    {
        var id = await SeedReview();
        var effect = new CountingEffect();
        var runner = new OrderMachine().CreateEffectRunner(
            new OrderMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims()),
            new IdempotentEffect(TestDb.NewClaims()),
            new ServiceCollection().AddSingleton<IOrderCharge>(effect).BuildServiceProvider()
        )!;

        (await runner.Run("u", id, "req-1")).Should().BeOfType<AdvanceOutcome.Advanced>();

        effect.Calls.Should().Be(1);
        (await StoredState(id)).Should().Be("Placed");
    }

    [Test]
    public async Task Autosave_refuses_to_create_a_draft_in_a_committed_state()
    {
        var id = Guid.NewGuid();

        (await Orders().Autosave("u", id, Placed("r-1")))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("state-reserved", Adr);
        (await Orders().Load("u", id)).Should().BeOfType<LoadResult.NotFound>();
    }

    [Test]
    public async Task Autosave_refuses_to_move_a_draft_into_a_committed_state()
    {
        var id = await SeedReview();

        (await Orders().Autosave("u", id, Placed("r-1")))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("state-reserved", Adr);
        (await StoredState(id)).Should().Be("Review");
    }

    [Test]
    public async Task Autosave_refuses_to_enter_an_effect_target_that_is_not_committed()
    {
        var service = new ShipMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims());
        var id = Guid.NewGuid();

        var shipped = new JsonObject
        {
            ["machine"] = "ship",
            ["version"] = 1,
            ["state"] = "Shipped",
            ["context"] = new JsonObject { ["receipt"] = "r-1" },
        }.ToJsonString();

        (await service.Autosave("u", id, shipped))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("state-reserved", Adr);
    }

    [Test]
    public async Task Autosave_keeps_a_same_state_save_of_a_draft_already_committed()
    {
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert("u", id, Snapshot(Placed("r-1")))).Should().BeTrue();

        (await Orders().Autosave("u", id, Placed("r-1"))).Should().BeOfType<AutosaveResult.Saved>();
    }

    [Test]
    public async Task Autosave_refuses_to_overwrite_a_stored_draft_that_does_not_rehydrate()
    {
        var id = Guid.NewGuid();
        // A Placed row with no receipt fails the Placed validator, so it cannot be read back.
        (await TestDb.NewStore().Upsert("u", id, Snapshot(Placed(""))))
            .Should()
            .BeTrue();

        (await Orders().Autosave("u", id, OrderMachine.ReviewSnapshot(1)))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("draft-unreadable", Adr);
        (await Orders().Load("u", id)).Should().BeOfType<LoadResult.Invalid>();
    }

    [Test]
    public async Task Autosave_resets_a_stored_draft_that_does_not_rehydrate_to_the_initial_state()
    {
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert("u", id, Snapshot(Placed("")))).Should().BeTrue();

        var fresh = new JsonObject
        {
            ["machine"] = "order",
            ["version"] = 1,
            ["state"] = "Draft",
            ["context"] = new JsonObject { ["items"] = new JsonArray(), ["receipt"] = null },
        }.ToJsonString();

        (await Orders().Autosave("u", id, fresh)).Should().BeOfType<AutosaveResult.Saved>();
        (await StoredState(id)).Should().Be("Draft");
    }

    private static Snapshot Snapshot(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        return new Snapshot
        {
            Machine = node["machine"]!.GetValue<string>(),
            Version = node["version"]!.GetValue<int>(),
            State = node["state"]!.GetValue<string>(),
            Context = node["context"]!.AsObject().DeepClone().AsObject(),
        };
    }
}
