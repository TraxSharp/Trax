namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

/// <summary>The turnstile as a system-owned machine: only <see cref="IMachineInstances.Start{TMachine}"/> creates one.</summary>
public sealed class SystemTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        TurnstileMachine.Declare(m, "system-turnstile");
        m.SystemOwned();
    }
}

/// <summary>The order, with its effect, as a system-owned machine.</summary>
public sealed class SystemOrderMachine : Machine<OrderState, OrderTrigger>
{
    protected override void Configure(IMachineBuilder<OrderState, OrderTrigger> m)
    {
        OrderMachine.Declare(m, "system-order");
        m.SystemOwned();
    }
}
