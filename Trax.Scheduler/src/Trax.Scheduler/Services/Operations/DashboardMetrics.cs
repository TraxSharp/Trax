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
