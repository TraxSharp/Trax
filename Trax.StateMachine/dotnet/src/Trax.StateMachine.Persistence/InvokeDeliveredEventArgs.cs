namespace Trax.StateMachine.Persistence;

/// <summary>What <see cref="InvokeOutcomeReconciler.Delivered"/> is raised with: the run's token and the delivery.</summary>
internal sealed class InvokeDeliveredEventArgs(string invokeToken, InvokeDelivery delivery)
    : EventArgs
{
    public string InvokeToken { get; } = invokeToken;

    public InvokeDelivery Delivery { get; } = delivery;
}
