using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// The owner reads an instance only while holding <see cref="ViewerReadMachine.Viewer"/>, but watches it without,
/// which would stream what <c>get</c> withholds.
/// </summary>
public class LooseWatchMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "loose-watch";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            get: r => r.OwnerOnly().Requires(ViewerReadMachine.Viewer),
            watch: r => r.OwnerOnly()
        );
}
