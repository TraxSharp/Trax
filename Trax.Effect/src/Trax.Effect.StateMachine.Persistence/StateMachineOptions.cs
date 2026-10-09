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
    /// miss it (a crash after the run, a run the reaper failed, a cancel before dispatch), and how often the
    /// sweep runs while the notifications are not being heard. Must be positive.
    /// </summary>
    public TimeSpan InvokeOutcomeSweepInterval
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often this host's outcome reconciler sweeps while it hears the provider's notification of invoked runs
    /// ending (Postgres): each notification delivers its run at once, and the reconciler sweeps in full whenever it
    /// subscribes again after losing them, so the sweep only has to catch what both miss. The default is 1 minute.
    /// Never shorter than <see cref="InvokeOutcomeSweepInterval"/>, which applies whenever the notifications are not
    /// heard. Must be positive.
    /// </summary>
    public TimeSpan InvokeOutcomeSweepIntervalWhileListening
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMinutes(1);
}
