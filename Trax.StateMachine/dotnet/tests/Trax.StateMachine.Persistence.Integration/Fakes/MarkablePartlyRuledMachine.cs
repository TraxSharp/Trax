using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>Leaves <c>Push</c>'s rule out, and is open for the same reason as <see cref="MarkableRuledMachine"/>.</summary>
public class MarkablePartlyRuledMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "markable-partly-ruled";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(m, Id, RuledTurnstile.AnyAuthenticated, RuledTurnstile.Undeclared);
}
