namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Selects the granularity of the executions-over-time series in
/// <see cref="DashboardMetrics"/>. The other series in the dashboard metrics block
/// (throughput, top failures, avg durations) are always over the last 7 days.
/// </summary>
public enum MetricsRange
{
    /// <summary>Last 60 minutes, 60 buckets, 1 minute each.</summary>
    Last60Minutes = 0,

    /// <summary>Last 24 hours, 24 buckets, 1 hour each.</summary>
    Last24Hours = 1,
}
