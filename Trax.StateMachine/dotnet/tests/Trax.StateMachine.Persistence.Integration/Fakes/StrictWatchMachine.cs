using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>Watches only with the role <c>get</c> needs, and one more: stricter than get, which is allowed.</summary>
public class StrictWatchMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "strict-watch";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            get: r => r.OwnerOnly().Requires(ViewerReadMachine.Viewer, DeskRoles.Operator),
            watch: r =>
                r.OwnerOnly().Requires(ViewerReadMachine.Viewer).Requires(DeskRoles.Operator)
        );
}
