using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>Declares <c>Coin</c>'s rule and leaves <c>Push</c>'s out.</summary>
public class PartlyRuledMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "partly-ruled";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(m, Id, RuledTurnstile.AnyAuthenticated, RuledTurnstile.Undeclared);
}
