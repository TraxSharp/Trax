using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary><c>Coin</c> requires a policy no host registers.</summary>
public class PolicyRuledMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "policy-ruled";
    public const string MissingPolicy = "no-such-desk-policy";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            e => e.RequiresPolicy(MissingPolicy),
            RuledTurnstile.AnyAuthenticated
        );
}
