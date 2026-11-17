using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The raw-delegate turnstile with every rule declared: it has no exportable IR.</summary>
public class RuledRawTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "ruled-raw-turnstile";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(TurnstileState.Locked, () => new JsonObject())
            .Start<DeskStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(TurnstileState.Locked)
            .On(TurnstileTrigger.Coin)
            .AnyAuthenticated()
            .When(
                (_, input) =>
                    input is JsonObject o
                    && o["coin"]?.GetValueKind() == JsonValueKind.String
                    && o["coin"].NotNull().GetValue<string>() is "quarter" or "dollar"
            )
            .Reduce(
                (_, input) =>
                    new JsonObject { ["paidWith"] = input.NotNull()["coin"].NotNull().DeepClone() }
            )
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .On(TurnstileTrigger.Push)
            .AnyAuthenticated()
            .Reduce((_, _) => new JsonObject())
            .To(TurnstileState.Locked);
    }
}
