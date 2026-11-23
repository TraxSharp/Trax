using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>Lets any authenticated caller discard, looser than owner-only.</summary>
public class LooseDiscardMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "loose-discard";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            discard: r => r.AnyAuthenticated()
        );
}
