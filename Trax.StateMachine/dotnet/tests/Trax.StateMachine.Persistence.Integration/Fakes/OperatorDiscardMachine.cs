using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The owner discards an instance only while holding <see cref="DeskRoles.Operator"/>.</summary>
public sealed class OperatorDiscardMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "operator-discard";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            discard: r => r.OwnerOnly().Requires(DeskRoles.Operator)
        );
}
