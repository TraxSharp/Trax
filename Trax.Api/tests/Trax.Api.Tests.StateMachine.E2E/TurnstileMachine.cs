using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

public sealed class TurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    private static readonly HashSet<string> Accepted = new(StringComparer.Ordinal)
    {
        "quarter",
        "dollar",
    };

    private static string? Coin(JsonNode? input) =>
        input is JsonObject o && o["coin"]?.GetValueKind() == JsonValueKind.String
            ? o["coin"].NotNull().GetValue<string>()
            : null;

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id("turnstile")
            .Version(1)
            .StartsAt(TurnstileState.Locked, () => new JsonObject())
            .Start<EmptyStart>(s => s.Reduce(Rules.Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(TurnstileState.Locked)
            .Holds(ctx => ctx.Count == 0 ? null : "Locked carries no context.")
            .On(TurnstileTrigger.Coin)
            .AnyAuthenticated()
            .When((_, input) => Accepted.Contains(Coin(input) ?? string.Empty))
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce((_, input) => new JsonObject { ["paidWith"] = Coin(input) })
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .Holds(ctx =>
                ctx["paidWith"]?.GetValueKind() == JsonValueKind.String
                && ctx["paidWith"].NotNull().GetValue<string>().Length > 0
                    ? null
                    : "Unlocked requires a non-empty paidWith."
            )
            .On(TurnstileTrigger.Push)
            .AnyAuthenticated()
            .Reduce((_, _) => new JsonObject())
            .To(TurnstileState.Locked);
    }

    /// <summary>A fresh Locked snapshot as canonical JSON, for seeding via the save mutation.</summary>
    public static readonly string InitialJson = new JsonObject
    {
        ["machine"] = "turnstile",
        ["version"] = 1,
        ["state"] = "Locked",
        ["context"] = new JsonObject(),
    }.ToJsonString();
}
