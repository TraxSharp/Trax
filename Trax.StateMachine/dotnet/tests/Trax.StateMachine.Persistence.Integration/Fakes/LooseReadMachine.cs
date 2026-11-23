using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>Lets anyone, signed in or not, read an instance through <c>get</c>.</summary>
public class LooseReadMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "loose-read";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            get: r => r.Anonymous()
        );
}
