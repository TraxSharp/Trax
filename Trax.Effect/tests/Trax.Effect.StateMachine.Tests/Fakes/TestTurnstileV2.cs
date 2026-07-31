using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Tests.Fakes;

/// <summary>
/// A version-2 turnstile used to exercise forward migration on rehydrate: a stored v1 snapshot is
/// upgraded to v2 (adds a <c>migrated</c> marker to Unlocked, keeps <c>paidWith</c>). Also provides a
/// v3-with-a-gap definition to prove a missing migration is a typed <c>version-mismatch</c>, never a
/// silent misread.
/// </summary>
public static class TestTurnstileV2
{
    public static readonly MachineDefinition<TurnstileState, TurnstileTrigger> Definition = new()
    {
        Id = TestTurnstile.Id,
        Version = 2,
        InitialState = TurnstileState.Locked,
        CreateInitialContext = () => new JsonObject(),
        Transitions = TestTurnstile.Definition.Transitions,
        ContextValidators = new Dictionary<TurnstileState, Func<JsonObject, string?>>
        {
            [TurnstileState.Locked] = ctx => ctx.Count == 0 ? null : "Locked carries no context.",
            // v2 Unlocked keeps the paidWith rule; the migration adds the marker so a migrated snapshot passes.
            [TurnstileState.Unlocked] = ctx =>
                ctx["paidWith"]?.GetValueKind() == JsonValueKind.String
                && ctx["paidWith"]!.GetValue<string>().Length > 0
                    ? null
                    : "Unlocked requires a non-empty paidWith.",
        },
        Migrations = new Dictionary<int, Func<string, JsonObject, MigrationResult>>
        {
            [1] = (state, ctx) =>
            {
                var next = (JsonObject)ctx.DeepClone();
                next["migrated"] = true;
                return new MigrationResult(state, next);
            },
        },
    };

    public static readonly SnapshotMachine<TurnstileState, TurnstileTrigger> Machine = new(
        Definition
    );

    /// <summary>A v3 definition with NO migration from v2 — a gap in the chain, proving version-mismatch.</summary>
    public static readonly SnapshotMachine<TurnstileState, TurnstileTrigger> V3WithGap = new(
        new MachineDefinition<TurnstileState, TurnstileTrigger>
        {
            Id = TestTurnstile.Id,
            Version = 3,
            InitialState = TurnstileState.Locked,
            CreateInitialContext = () => new JsonObject(),
            Transitions = TestTurnstile.Definition.Transitions,
            Migrations = new Dictionary<int, Func<string, JsonObject, MigrationResult>>
            {
                // Only 1 -> 2 is registered; 2 -> 3 is missing on purpose.
                [1] = (state, ctx) => new MigrationResult(state, (JsonObject)ctx.DeepClone()),
            },
        }
    );
}
