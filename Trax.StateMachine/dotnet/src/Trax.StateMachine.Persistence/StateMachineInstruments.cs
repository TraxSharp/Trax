namespace Trax.StateMachine.Persistence;

/// <summary>The meter and instrument names the state-machine subsystem reports through.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/add-state-machines#census-and-instruments">SDK reference</seealso>
public static class StateMachineInstruments
{
    /// <summary>The diagnostic meter name.</summary>
    public const string MeterName = "Trax.StateMachine";

    /// <summary>Counts advances refused because the client's twin computed a different result than the server.</summary>
    public const string ClientDivergence = "trax.machine.client_divergence";

    /// <summary>Counts due timers that could not be fired.</summary>
    public const string TimerFireFailed = "trax.machine.timer.fire_failed";

    /// <summary>
    /// Counts each attempt to fire a due timer that threw. The attempt rolls back alone and is tried again after a
    /// back-off; after the last attempt the timer is cleared and counted on <see cref="TimerFireFailed"/>.
    /// </summary>
    public const string TimerFireErrors = "trax.machine.timer.fire_errors";

    /// <summary>Counts snapshot change notices whose payload is not a row id. Each one is dropped.</summary>
    public const string SnapshotNoticeMalformed = "trax.machine.snapshot_notice.malformed";

    /// <summary>
    /// Reports how full the database's notification queue is, from 0 to 1, as the snapshot change listener last read
    /// it (<c>pg_notification_queue_usage()</c>, Postgres only). The listener reads it every 30 seconds while it
    /// listens, however many notices arrive.
    /// </summary>
    public const string NotificationQueueUsage = "trax.machine.snapshot_notice.queue_usage";

    /// <summary>
    /// Counts effects found pending past their claimant's lease. A host that registers the effect takes each over and
    /// runs it again under the same key; one that does not leaves it, and it is counted again on each sweep until a
    /// host finishes it.
    /// </summary>
    public const string EffectsAbandoned = "trax.machine.effect.abandoned";
}
