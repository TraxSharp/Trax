using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// The declarative turnstile as a system-owned machine, so a host's trusted advance through
/// <see cref="IMachineInstances"/> fires a trigger that declares its input.
/// </summary>
public sealed class SystemDeclarativeTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "system-declarative-turnstile";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        DeclarativeTurnstileMachine.ConfigureTurnstile(m, Id);
        m.SystemOwned();
    }
}
