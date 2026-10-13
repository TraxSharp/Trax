using System.Text.Json.Nodes;
using AwesomeAssertions;
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

public enum LedgerState
{
    Open,
    Counting,
    Closed,
}

public enum LedgerTrigger
{
    Count,
    Close,
    Reopen,
}

/// <summary>
/// A ledger whose <c>Closed</c> state is committed and which has no effect, invoked train or role-gated event, so
/// the soft save still serves it: <c>Open -Count-&gt; Counting -Close-&gt; Closed -Reopen-&gt; Open</c>. A closed
/// ledger holds at least one entry, so a stored one without any cannot be read back.
/// </summary>
public sealed class LedgerMachine : Machine<LedgerState, LedgerTrigger>
{
    public const string Id = "ledger";

    private static JsonObject Fresh() => new() { ["entries"] = new JsonArray() };

    protected override void Configure(IMachineBuilder<LedgerState, LedgerTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(LedgerState.Open, Fresh);
        m.In(LedgerState.Open)
            .Holds(ctx => ctx["entries"] is JsonArray ? null : "Open: entries[].")
            .On(LedgerTrigger.Count)
            .To(LedgerState.Counting);
        m.In(LedgerState.Counting)
            .Holds(ctx => ctx["entries"] is JsonArray ? null : "Counting: entries[].")
            .On(LedgerTrigger.Close)
            .To(LedgerState.Closed);
        m.In(LedgerState.Closed)
            .Committed()
            .Holds(ctx =>
                ctx["entries"] is JsonArray { Count: > 0 } ? null : "Closed: at least one entry."
            )
            .On(LedgerTrigger.Reopen)
            .Reduce((_, _) => Fresh())
            .To(LedgerState.Open);
    }

    /// <summary>A ledger in <paramref name="state"/> holding <paramref name="entries"/>, as a client sends it.</summary>
    public static string Json(LedgerState state, params int[] entries) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state.ToString(),
            ["context"] = new JsonObject
            {
                ["entries"] = new JsonArray(entries.Select(e => (JsonNode)e).ToArray()),
            },
        }.ToJsonString();
}

/// <summary>
/// Which writes the draft operations accept. A state that an effect-bound transition lands in, or that is
/// committed, is reached only by the effect runner: the advance path refuses an effect-bound trigger. The soft
/// save does not serve a machine with an effect at all (<c>save-not-allowed</c>), and on a machine it serves it
/// refuses to create or move a draft into a committed state, to rewrite one there, or to overwrite a stored draft it
/// cannot read.
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

    // The soft save does not serve a machine with an effect, so its drafts are seeded through the store.
    private static async Task<Guid> SeedReview()
    {
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert("u", id, Snapshot(OrderMachine.ReviewSnapshot(1))))
            .Should()
            .BeTrue();
        return id;
    }

    private static async Task<string?> StoredState(Guid id) =>
        await Orders().Load("u", id) is LoadResult.Loaded loaded ? loaded.Snapshot.State : null;

    private static ISnapshotDraftService Ledgers() =>
        new LedgerMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims());

    private static async Task<JsonObject?> StoredLedger(Guid id) =>
        await Ledgers().Load("u", id) is LoadResult.Loaded loaded ? loaded.Snapshot.Context : null;

    private const string NoSave =
        "the soft save serves only machines with no server-side behaviour, and an effect is one. See "
        + "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

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

        (await Ledgers().Autosave("u", id, LedgerMachine.Json(LedgerState.Closed, 1)))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("state-reserved", Adr);
        (await Ledgers().Load("u", id)).Should().BeOfType<LoadResult.NotFound>();
    }

    [Test]
    public async Task Autosave_refuses_to_move_a_draft_into_a_committed_state()
    {
        var id = Guid.NewGuid();
        (await Ledgers().Autosave("u", id, LedgerMachine.Json(LedgerState.Counting, 1)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        (await Ledgers().Autosave("u", id, LedgerMachine.Json(LedgerState.Closed, 1)))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("state-reserved", Adr);
        (await Ledgers().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be(nameof(LedgerState.Counting));
    }

    [Test]
    public async Task Autosave_refuses_every_save_of_a_machine_with_an_effect()
    {
        var id = await SeedReview();

        (await Orders().Autosave("u", id, Placed("r-1")))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be(DraftRefusalCodes.SaveNotAllowed, NoSave);
        (await Orders().Autosave("u", id, OrderMachine.ReviewSnapshot(1, 2)))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be(DraftRefusalCodes.SaveNotAllowed, NoSave);
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
            .Be(DraftRefusalCodes.SaveNotAllowed, NoSave);
        (await service.Load("u", id)).Should().BeOfType<LoadResult.NotFound>();
    }

    [Test]
    public async Task Autosave_must_not_rewrite_a_committed_draft()
    {
        var id = Guid.NewGuid();
        (
            await TestDb
                .NewStore()
                .Upsert("u", id, Snapshot(LedgerMachine.Json(LedgerState.Closed, 1)))
        )
            .Should()
            .BeTrue();

        (await Ledgers().Autosave("u", id, LedgerMachine.Json(LedgerState.Closed, 1, 2, 3)))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("draft-committed", Adr);
        (await StoredLedger(id))!["entries"]!.AsArray().Count.Should().Be(1);
    }

    [Test]
    public async Task Autosave_accepts_a_save_of_a_committed_draft_that_changes_nothing()
    {
        var id = Guid.NewGuid();
        (
            await TestDb
                .NewStore()
                .Upsert("u", id, Snapshot(LedgerMachine.Json(LedgerState.Closed, 1)))
        )
            .Should()
            .BeTrue();

        (await Ledgers().Autosave("u", id, LedgerMachine.Json(LedgerState.Closed, 1)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
    }

    [Test]
    public async Task Autosave_must_not_rewrite_a_draft_in_an_effects_target()
    {
        var service = new ShipMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims());
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert("u", id, Snapshot(Shipped("r-1")))).Should().BeTrue();

        (await service.Autosave("u", id, Shipped("forged")))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be(DraftRefusalCodes.SaveNotAllowed, NoSave);
        var stored = (LoadResult.Loaded)await service.Load("u", id);
        stored.Snapshot.Context["receipt"]!.GetValue<string>().Should().Be("r-1");
    }

    [Test]
    public async Task Autosave_refuses_a_reset_the_machine_does_not_declare_from_an_effects_target()
    {
        var service = new ShipMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims());
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert("u", id, Snapshot(Shipped("r-1")))).Should().BeTrue();
        var packing = new JsonObject
        {
            ["machine"] = "ship",
            ["version"] = 1,
            ["state"] = "Packing",
            ["context"] = new JsonObject(),
        }.ToJsonString();

        (await service.Autosave("u", id, packing))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be(DraftRefusalCodes.SaveNotAllowed, NoSave);
        ((LoadResult.Loaded)await service.Load("u", id))
            .Snapshot.State.Should()
            .Be(nameof(ShipState.Shipped));
    }

    private static string Shipped(string receipt) =>
        new JsonObject
        {
            ["machine"] = "ship",
            ["version"] = 1,
            ["state"] = "Shipped",
            ["context"] = new JsonObject { ["receipt"] = receipt },
        }.ToJsonString();

    [Test]
    public async Task Autosave_refuses_to_overwrite_a_stored_draft_that_does_not_rehydrate()
    {
        var id = Guid.NewGuid();
        // A Closed row with no entries fails the Closed validator, so it cannot be read back.
        (await TestDb.NewStore().Upsert("u", id, Snapshot(LedgerMachine.Json(LedgerState.Closed))))
            .Should()
            .BeTrue();

        (await Ledgers().Autosave("u", id, LedgerMachine.Json(LedgerState.Counting, 1)))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("draft-unreadable", Adr);
        (await Ledgers().Load("u", id)).Should().BeOfType<LoadResult.Invalid>();
    }

    [Test]
    public async Task Autosave_resets_a_stored_draft_that_does_not_rehydrate_to_the_initial_state()
    {
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert("u", id, Snapshot(LedgerMachine.Json(LedgerState.Closed))))
            .Should()
            .BeTrue();

        (await Ledgers().Autosave("u", id, LedgerMachine.Json(LedgerState.Open)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        (await Ledgers().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be(nameof(LedgerState.Open));
    }

    private const string MachinesAreTheApi =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static ISnapshotMachineRegistry Registry(params IMachine[] machines)
    {
        var context = TestDb.NewContext();
        return new SnapshotMachineRegistry(
            machines,
            TestDb.NewStore(context),
            TestDb.NewClaims(context),
            new IdempotentEffect(TestDb.NewClaims(context)),
            new ServiceCollection().BuildServiceProvider()
        );
    }

    private static Task<SaveSnapshotOutput> GenericSave(
        IMachine machine,
        string user,
        Guid id,
        string snapshot
    ) =>
        new SaveSnapshotJunction(Registry(machine), new FakePrincipal(user)).Run(
            new SaveSnapshotInput
            {
                Machine = machine.Name,
                Id = id,
                Snapshot = snapshot,
            }
        );

    [Test]
    [Property("adr", MachinesAreTheApi)]
    public async Task The_generic_save_is_refused_for_a_machine_with_a_Requires_rule()
    {
        var user = $"u-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        var draft = new JsonObject
        {
            ["machine"] = RuledDeskMachine.Id,
            ["version"] = 1,
            ["state"] = nameof(DeskState.Draft),
            ["context"] = new JsonObject { ["title"] = "desk" },
        }.ToJsonString();

        var output = await GenericSave(new RuledDeskMachine(), user, id, draft);

        output.Problem.Should().NotBeNull("the save is refused");
        output
            .Problem!.Code.Should()
            .Be(
                DraftRefusalCodes.SaveNotAllowed,
                $"a gated machine's states are reached through its declared events only. See {MachinesAreTheApi}"
            );
        (await TestDb.NewStore().Get(user, id)).Should().BeNull("a refused save writes nothing");
    }

    [Test]
    [Property("adr", MachinesAreTheApi)]
    public async Task The_generic_save_still_works_for_a_plain_machine()
    {
        var user = $"u-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();

        var output = await GenericSave(
            new TurnstileMachine(),
            user,
            id,
            TestTurnstile.UnlockedJson
        );

        output
            .Problem.Should()
            .BeNull(
                $"a machine with no rule, timer, invoke or effect keeps its save. See {MachinesAreTheApi}"
            );
        (await TestDb.NewStore().Get(user, id)).Should().NotBeNull();
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
