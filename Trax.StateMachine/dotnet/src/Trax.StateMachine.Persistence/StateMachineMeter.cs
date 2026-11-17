namespace Trax.StateMachine.Persistence;

/// <summary>The state-machine subsystem's meter and the instruments it reports through.</summary>
internal static class StateMachineMeter
{
    private static readonly System.Diagnostics.Metrics.Meter Meter = new(
        StateMachineInstruments.MeterName
    );

    /// <summary>One per advance refused as <c>CLIENT_DIVERGENCE</c>, tagged with the machine.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> ClientDivergence =
        Meter.CreateCounter<long>(
            StateMachineInstruments.ClientDivergence,
            description: "Advances refused because the client's twin computed a different result than the server."
        );

    /// <summary>
    /// One per due timer that could not be fired (its target refused the context, the run it would queue could not
    /// be, or its fire threw on every attempt), tagged with the machine. Its due time is cleared, so it is counted
    /// once.
    /// </summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> TimerFireFailed =
        Meter.CreateCounter<long>(
            StateMachineInstruments.TimerFireFailed,
            description: "Due timers that could not be fired; each one's due time is cleared."
        );

    /// <summary>One per effect found pending past its claimant's lease, tagged with the machine.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> EffectsAbandoned =
        Meter.CreateCounter<long>(
            StateMachineInstruments.EffectsAbandoned,
            description: "Effects found pending past their claimant's lease."
        );

    /// <summary>One per attempt to fire a due timer that threw, tagged with the machine.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> TimerFireErrors =
        Meter.CreateCounter<long>(
            StateMachineInstruments.TimerFireErrors,
            description: "Attempts to fire a due timer that threw; each one is tried again after a back-off."
        );

    /// <summary>One per snapshot change notice whose payload is not a row id; the notice is dropped.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> SnapshotNoticeMalformed =
        Meter.CreateCounter<long>(
            StateMachineInstruments.SnapshotNoticeMalformed,
            description: "Snapshot change notices dropped because their payload is not a row id."
        );

    // The latest pg_notification_queue_usage() each live Postgres change listener read, by listener.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        object,
        double
    > QueueUsages = new();

    /// <summary>
    /// The fullest notification queue any live change listener in the process last read, from 0 to 1. Nothing is
    /// reported until one has read it. Postgres stops delivering notices when the queue is full, so alert well
    /// before 1.
    /// </summary>
    public static readonly System.Diagnostics.Metrics.ObservableGauge<double> NotificationQueueUsage =
        Meter.CreateObservableGauge(
            StateMachineInstruments.NotificationQueueUsage,
            ObserveQueueUsage,
            unit: "{ratio}",
            description: "How full the database's notification queue is (pg_notification_queue_usage()), from 0 to 1."
        );

    /// <summary>Records <paramref name="usage"/> as the latest reading of <paramref name="listener"/>.</summary>
    public static void ReportQueueUsage(object listener, double usage) =>
        QueueUsages[listener] = usage;

    /// <summary>Stops reporting <paramref name="listener"/>'s reading, when it stops listening.</summary>
    public static void ForgetQueueUsage(object listener) => QueueUsages.TryRemove(listener, out _);

    private static IEnumerable<System.Diagnostics.Metrics.Measurement<double>> ObserveQueueUsage()
    {
        if (QueueUsages.IsEmpty)
            return [];
        return [new(QueueUsages.Values.Max())];
    }
}
