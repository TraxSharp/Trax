namespace Trax.StateMachine.Persistence;

/// <summary>What <see cref="InvokeOutcomeReconciler.TimerFired"/> is raised with: what the reconciler did with a timer.</summary>
internal sealed class TimerFiredEventArgs(TimerFireResult result) : EventArgs
{
    public TimerFireResult Result { get; } = result;
}
