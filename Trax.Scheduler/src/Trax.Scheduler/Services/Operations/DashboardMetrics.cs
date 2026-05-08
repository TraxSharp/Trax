namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Snapshot of dashboard-relevant metrics. Returned by
/// <see cref="IOperationsService.GetDashboardMetricsAsync"/>; consumed by the dashboard
/// Index page and the GraphQL <c>operations.metrics.dashboard</c> query.
/// </summary>
public record DashboardMetrics(
    DashboardKpis Kpis,
    IReadOnlyList<ExecutionsBucket> ExecutionsOverTime,
    IReadOnlyList<TrainFailureCount> TopFailures,
    IReadOnlyList<TrainAverageDuration> TopAverageDurations,
    IReadOnlyList<ThroughputSeries> ThroughputSeries
);

/// <param name="ExecutionsToday">Total executions started today (UTC), all states.</param>
/// <param name="SuccessRate">Completed / (Completed + Failed) as a percentage. Zero when no terminal executions.</param>
/// <param name="CurrentlyRunning">Executions in <c>InProgress</c> state right now.</param>
/// <param name="UnresolvedDeadLetters">Dead letters in <c>AwaitingIntervention</c> state.</param>
public record DashboardKpis(
    int ExecutionsToday,
    double SuccessRate,
    int CurrentlyRunning,
    int UnresolvedDeadLetters
);

/// <summary>
/// One bucket of the executions-over-time chart, broken down by terminal state.
/// </summary>
/// <param name="Timestamp">UTC start of the bucket.</param>
/// <param name="Completed">Count of completed executions in this bucket.</param>
/// <param name="Failed">Count of failed executions.</param>
/// <param name="Cancelled">Count of cancelled executions.</param>
public record ExecutionsBucket(DateTime Timestamp, int Completed, int Failed, int Cancelled);

/// <param name="TrainName">Train interface FullName as stored on <c>Metadata.Name</c>.</param>
/// <param name="Count">Number of failures over the time window (last 7 days).</param>
public record TrainFailureCount(string TrainName, int Count);

/// <param name="TrainName">Train interface FullName.</param>
/// <param name="AverageMilliseconds">Mean execution time over completed root-level runs in the last 7 days.</param>
public record TrainAverageDuration(string TrainName, double AverageMilliseconds);

/// <summary>
/// One per-train series for the 7-day throughput sparkline. The dashboard renders the
/// top 3 trains plus an "Other" series. The service emits the top-N (default 3) series
/// plus an "Other" bucket in the same shape; consumers can render however they want.
/// </summary>
/// <param name="TrainName">
/// Train interface FullName, or the literal string <c>"Other"</c> for the aggregated
/// remainder series.
/// </param>
/// <param name="Buckets">28 buckets of 6 hours each, oldest first.</param>
public record ThroughputSeries(string TrainName, IReadOnlyList<ThroughputBucket> Buckets);

/// <param name="Timestamp">UTC start of the bucket.</param>
/// <param name="Count">Number of completed executions in the bucket.</param>
public record ThroughputBucket(DateTime Timestamp, int Count);

/// <summary>
/// Process-level health snapshot. Memory/GC/uptime are exact at call time; CPU%
/// requires consumer-side sampling state and is intentionally omitted from the shared
/// service.
/// </summary>
/// <param name="ProcessStartTimeUtc">When the host process started.</param>
/// <param name="UptimeSeconds">Seconds since process start.</param>
/// <param name="WorkingSetBytes">Resident set size (Process.WorkingSet64).</param>
/// <param name="GcHeapBytes">Total GC-managed heap (GC.GetTotalMemory(false)).</param>
public record ServerMetrics(
    DateTime ProcessStartTimeUtc,
    double UptimeSeconds,
    long WorkingSetBytes,
    long GcHeapBytes
);
