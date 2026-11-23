namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The order, with its effect, as a system-owned machine.</summary>
public sealed class SystemOrderMachine : Machine<OrderState, OrderTrigger>
{
    protected override void Configure(IMachineBuilder<OrderState, OrderTrigger> m)
    {
        OrderMachine.Declare(m, "system-order");
        m.SystemOwned();
    }
}
