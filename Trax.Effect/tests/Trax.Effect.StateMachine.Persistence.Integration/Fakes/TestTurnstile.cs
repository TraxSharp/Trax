using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

public enum TurnstileState
{
    Locked,
    Unlocked,
}

public enum TurnstileTrigger
{
    Coin,
    Push,
}

/// <summary>A turnstile with no committed states — exercises the fast (last-writer-wins) autosave path.</summary>
public static class TestTurnstile
{
    private static readonly HashSet<string> AcceptedCoins = new(StringComparer.Ordinal)
    {
        "quarter",
        "dollar",
    };

    private static string? Coin(JsonNode? input) =>
        input is JsonObject o && o["coin"]?.GetValueKind() == JsonValueKind.String
            ? o["coin"]!.GetValue<string>()
            : null;

    public static readonly MachineDefinition<TurnstileState, TurnstileTrigger> Definition = new()
    {
        Id = "turnstile",
        Version = 1,
        InitialState = TurnstileState.Locked,
        CreateInitialContext = () => new JsonObject(),
        Transitions = new[]
        {
            new TransitionDefinition<TurnstileState, TurnstileTrigger>
            {
                From = TurnstileState.Locked,
                Trigger = TurnstileTrigger.Coin,
                To = TurnstileState.Unlocked,
                Guard = (_, input) => AcceptedCoins.Contains(Coin(input) ?? string.Empty),
                GuardMessage = "Only a quarter or a dollar is accepted.",
                Reduce = (_, input) => new JsonObject { ["paidWith"] = Coin(input) },
            },
            new TransitionDefinition<TurnstileState, TurnstileTrigger>
            {
                From = TurnstileState.Unlocked,
                Trigger = TurnstileTrigger.Push,
                To = TurnstileState.Locked,
                Reduce = (_, _) => new JsonObject(),
            },
        },
        ContextValidators = new Dictionary<TurnstileState, Func<JsonObject, string?>>
        {
            [TurnstileState.Locked] = ctx => ctx.Count == 0 ? null : "Locked carries no context.",
            [TurnstileState.Unlocked] = ctx =>
                ctx["paidWith"]?.GetValueKind() == JsonValueKind.String
                && ctx["paidWith"]!.GetValue<string>().Length > 0
                    ? null
                    : "Unlocked requires a non-empty paidWith.",
        },
    };

    public static readonly SnapshotMachine<TurnstileState, TurnstileTrigger> Machine = new(
        Definition
    );

    public static SnapshotDraftService<TurnstileState, TurnstileTrigger> Service(
        ISnapshotStore store
    ) => new(Machine, store);

    public static string InitialJson =>
        Machine.Serialize(Machine.Definition.CreateInitialSnapshot());

    public static string UnlockedJson =>
        Machine.Serialize(
            new Snapshot
            {
                Machine = "turnstile",
                Version = 1,
                State = "Unlocked",
                Context = new JsonObject { ["paidWith"] = "quarter" },
            }
        );
}
