using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// The declarative turnstile with each event's rule and the reads supplied by the caller, so a startup test can
/// declare exactly the gap it is about.
/// </summary>
internal static class RuledTurnstile
{
    internal delegate ITransitionBuilder<TurnstileState, TurnstileTrigger> EventRule(
        ITransitionBuilder<TurnstileState, TurnstileTrigger> edge
    );

    internal static ITransitionBuilder<TurnstileState, TurnstileTrigger> Undeclared(
        ITransitionBuilder<TurnstileState, TurnstileTrigger> edge
    ) => edge;

    internal static ITransitionBuilder<TurnstileState, TurnstileTrigger> AnyAuthenticated(
        ITransitionBuilder<TurnstileState, TurnstileTrigger> edge
    ) => edge.AnyAuthenticated();

    internal static void Declare(
        IMachineBuilder<TurnstileState, TurnstileTrigger> m,
        string id,
        EventRule coin,
        EventRule push,
        bool declareReads = true,
        Action<IReadRuleBuilder>? get = null,
        Action<IReadRuleBuilder>? discard = null,
        bool declareDiscard = true,
        Action<IReadRuleBuilder>? watch = null
    )
    {
        m.Id(id).Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        if (declareReads)
            m.Start<DeskStart>(s => s.Reduce(Keep()).AnyAuthenticated())
                .Get(get ?? (r => r.OwnerOnly()))
                .Watch(watch ?? get ?? (r => r.OwnerOnly()));
        if (declareReads && declareDiscard)
            m.Discard(discard ?? (r => r.OwnerOnly()));

        coin(m.In(TurnstileState.Locked).Context().On(TurnstileTrigger.Coin))
            .WithInput<DeclarativeTurnstileMachine.CoinInput>()
            .When(
                Input((DeclarativeTurnstileMachine.CoinInput i) => i.Coin)
                    .IsOneOf("quarter", "dollar")
            )
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce(
                Set((DeclarativeTurnstileMachine.UnlockedContext u) => u.PaidWith)
                    .FromInput((DeclarativeTurnstileMachine.CoinInput i) => i.Coin)
            )
            .To(TurnstileState.Unlocked);

        push(
                m.In(TurnstileState.Unlocked)
                    .Context<DeclarativeTurnstileMachine.UnlockedContext>()
                    .On(TurnstileTrigger.Push)
            )
            .Reduce(Clear())
            .To(TurnstileState.Locked);
    }
}
