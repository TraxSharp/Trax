using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Cli.Tests.Fakes;

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
