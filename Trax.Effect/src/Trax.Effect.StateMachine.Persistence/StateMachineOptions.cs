namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Host-level options for the state-machine subsystem, configured through the
/// <see cref="StateMachinesBuilderExtensions.AddStateMachines(Trax.Effect.Configuration.TraxBuilder.TraxBuilderWithEffects, System.Action{StateMachineOptions}, System.Reflection.Assembly[])"/>
/// overload and read by the machine registry when it builds a per-machine draft service.
/// </summary>
public sealed class StateMachineOptions
{
    /// <summary>
    /// How long a draft survives without activity before the next <c>Load</c> discards it and the user
    /// starts fresh (a sliding window on the row's last update). The stale row is deleted, so an abandoned
    /// or long-completed draft can't linger or block a new one. <c>null</c> (the default) never expires a
    /// draft. Recommended: 7-30 days for a form-style flow.
    /// </summary>
    public TimeSpan? DraftTtl { get; set; }

    /// <summary>
    /// How often this host's outcome reconciler sweeps for invoked runs that have ended and applies each one's
    /// outcome to the state that invoked it. The default is 5 seconds. The sweep is the guarantee, not the usual
    /// path: the host that ran the train applies the outcome as soon as it is recorded, and on Postgres a
    /// notification wakes every host's reconciler sooner. It decides how long an outcome waits when both of those
    /// miss it (a crash after the run, a run the reaper failed, a cancel before dispatch). Must be positive.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalIds.Invokes)]
    public TimeSpan InvokeOutcomeSweepInterval
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(5);
}
