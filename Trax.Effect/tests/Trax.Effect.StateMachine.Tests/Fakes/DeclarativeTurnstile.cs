using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.Fakes;

/// <summary>
/// The turnstile authored with the DECLARATIVE, string-free surface: context records (schema + constraints),
/// member-expression guards, and declarative reducers, instead of raw delegates. It must behave identically
/// to <see cref="TestTurnstile"/> down to the byte (proven by the differential corpus replay) and export the
/// committed IR golden. This is the Increment-A anchor: the readable authoring produces the same data the raw
/// records did, runs through the untouched engine, and is faithfully exported.
/// </summary>
public static class DeclarativeTurnstile
{
    public sealed record UnlockedContext
    {
        [MinLength(1)]
        public string PaidWith { get; init; } = "";
    }

    public sealed record CoinInput
    {
        public string Coin { get; init; } = "";
    }

    public static readonly BuiltMachine<TurnstileState, TurnstileTrigger> Built = Build();
    public static readonly SnapshotMachine<TurnstileState, TurnstileTrigger> Machine = Built.Engine;

    private static BuiltMachine<TurnstileState, TurnstileTrigger> Build()
    {
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("turnstile").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .WithInput<CoinInput>()
            .When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput((CoinInput i) => i.Coin))
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .Context<UnlockedContext>()
            .On(TurnstileTrigger.Push)
            .Reduce(Clear())
            .To(TurnstileState.Locked);

        return m.Build();
    }
}
