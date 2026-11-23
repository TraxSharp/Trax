namespace Trax.Scheduler.Services.Operations;

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
