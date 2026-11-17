using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>Declares every rule but <c>discard</c>'s.</summary>
public class UndeclaredDiscardMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "undeclared-discard";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            declareDiscard: false
        );
}
