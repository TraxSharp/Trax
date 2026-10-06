using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Cli.Tests.Fakes;

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
/// A declaratively-authored turnstile, identical to the committed <c>turnstile</c> machine in
/// Trax.Api.StateMachine, so the CLI's generated IR / twin / corpus can be byte-compared against the committed
/// artifacts. The CLI test points <c>--assembly</c> at this test assembly and loads this machine by reflection,
/// exercising the exact path a real consumer uses.
/// </summary>
public sealed class DeclarativeTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
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

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
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

        m.Differential(d =>
            d.Sample(TurnstileTrigger.Coin, new CoinInput { Coin = "quarter" })
                .Sample(TurnstileTrigger.Coin, new CoinInput { Coin = "dollar" })
                .Sample(TurnstileTrigger.Coin, new CoinInput { Coin = "penny" })
                .EmptySample(TurnstileTrigger.Coin)
        );
    }
}

/// <summary>A second machine, so the CLI's "which machine?" selection logic has something to disambiguate.</summary>
public sealed class SecondTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id("turnstile-two").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .WithInput<DeclarativeTurnstileMachine.CoinInput>()
            .When(Input((DeclarativeTurnstileMachine.CoinInput i) => i.Coin).IsOneOf("quarter"))
            .Reduce(
                Set((DeclarativeTurnstileMachine.UnlockedContext u) => u.PaidWith)
                    .FromInput((DeclarativeTurnstileMachine.CoinInput i) => i.Coin)
            )
            .To(TurnstileState.Unlocked);
        m.In(TurnstileState.Unlocked)
            .Context<DeclarativeTurnstileMachine.UnlockedContext>()
            .On(TurnstileTrigger.Push)
            .Reduce(Clear())
            .To(TurnstileState.Locked);
    }
}
