using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>Every event declares a rule; <c>start</c>, <c>get</c> and the push (<c>&lt;machine&gt;Changed</c>) declare none.</summary>
public class UndeclaredReadsMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "undeclared-reads";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            declareReads: false
        );
}
