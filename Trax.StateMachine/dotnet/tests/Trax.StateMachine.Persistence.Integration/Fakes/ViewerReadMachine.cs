using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The owner reads an instance only while holding <see cref="Viewer"/>.</summary>
public sealed class ViewerReadMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "viewer-read";
    public const string Viewer = "Viewer";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            get: r => r.OwnerOnly().Requires(Viewer)
        );
}
