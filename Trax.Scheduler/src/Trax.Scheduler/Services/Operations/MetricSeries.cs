namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Runs over time for one train, manifest or group: per bucket, how many finished in each state
/// and how long the completed ones took. Returned by
/// <see cref="IOperationsService.GetMetricSeriesAsync"/>.
/// </summary>
/// <param name="Scope">What the runs are of.</param>
/// <param name="Range">The window.</param>
/// <param name="From">The first bucket's start, in UTC.</param>
/// <param name="To">The last bucket's end, in UTC: the end of the bucket holding now.</param>
/// <param name="BucketSeconds">How long each bucket is.</param>
/// <param name="Buckets">Every bucket in the window, oldest first, empty ones included.</param>
public sealed record MetricSeries(
    MetricSeriesScope Scope,
    MetricSeriesRange Range,
    DateTime From,
    DateTime To,
    int BucketSeconds,
    IReadOnlyList<MetricSeriesBucket> Buckets
);
