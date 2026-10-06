using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Tests.Fakes;

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

/// <summary>
/// A self-contained turnstile machine used by the engine unit tests. It exercises a guard
/// (accepted coins), a reducer that records input (recordCoin), a reducer that clears context
/// (clear), and per-state context validators that both require and forbid fields. Behavior mirrors
/// the shared turnstile fixtures so the engine tests and the cross-language conformance tests agree.
/// </summary>
public static class TestTurnstile
{
    public const string Id = "turnstile";

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
        Id = Id,
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
}
