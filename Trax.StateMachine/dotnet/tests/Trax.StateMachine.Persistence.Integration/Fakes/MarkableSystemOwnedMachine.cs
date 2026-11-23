using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// Declares every rule and is system-owned, and is open for the same reason as <see cref="MarkableRuledMachine"/>.
/// </summary>
public class MarkableSystemOwnedMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "markable-system-owned";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated
        );
        m.SystemOwned();
    }
}
