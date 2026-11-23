using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>A user-owned machine whose events admit anonymous callers.</summary>
public class AnonymousUserMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "anonymous-user";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(m, Id, e => e.Anonymous(), e => e.Anonymous());
}
