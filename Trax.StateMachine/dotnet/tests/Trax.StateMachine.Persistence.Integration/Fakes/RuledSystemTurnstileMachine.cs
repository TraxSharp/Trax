using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// Fully declared, with <c>Coin</c> requiring Operator, and system-owned: only code creates and drives one.
/// </summary>
public class RuledSystemTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "ruled-system-turnstile";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        RuledTurnstile.Declare(
            m,
            Id,
            e => e.Requires(DeskRoles.Operator),
            RuledTurnstile.AnyAuthenticated
        );
        m.SystemOwned();
    }
}
