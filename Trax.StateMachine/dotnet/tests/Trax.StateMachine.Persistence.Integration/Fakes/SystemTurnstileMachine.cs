namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The turnstile as a system-owned machine: only <see cref="IMachineInstances.Start{TMachine}"/> creates one.</summary>
public sealed class SystemTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        TurnstileMachine.Declare(m, "system-turnstile");
        m.SystemOwned();
    }
}
