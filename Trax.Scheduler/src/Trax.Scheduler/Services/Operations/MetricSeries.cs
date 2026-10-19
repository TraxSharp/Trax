namespace Trax.Scheduler.Services.Operations;

/// <summary>What a <see cref="MetricSeries"/> counts the runs of.</summary>
public enum MetricSeriesScope
{
    /// <summary>Every run of one train, named by its interface FullName.</summary>
    Train = 0,

    /// <summary>The runs of one manifest.</summary>
    Manifest = 1,

    /// <summary>The runs of every manifest in one manifest group.</summary>
    Group = 2,
}

/// <summary>The window a <see cref="MetricSeries"/> covers, and so its bucket size.</summary>
public enum MetricSeriesRange
{
    /// <summary>The last hour, 60 buckets of one minute.</summary>
    LastHour = 0,

    /// <summary>The last 24 hours, 96 buckets of 15 minutes.</summary>
    Last24Hours = 1,

    /// <summary>The last 7 days, 168 buckets of one hour.</summary>
    Last7Days = 2,

    /// <summary>
    /// The last 30 days, 120 buckets of 6 hours. Refused today by
    /// <see cref="OperationsService.MetricSeriesRefusal"/>: the existing indexes cannot serve it
    /// inside the metrics budget at a million runs.
    /// </summary>
    Last30Days = 3,
}

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

/// <summary>
/// One bucket of a <see cref="MetricSeries"/>. A run belongs to the bucket its start time falls
/// in. Runs still pending or in progress are not counted.
/// </summary>
/// <param name="Start">The bucket's start, in UTC.</param>
/// <param name="Completed">Runs that completed.</param>
/// <param name="Failed">Runs that failed.</param>
/// <param name="Cancelled">Runs that were cancelled.</param>
/// <param name="P50DurationMs">
/// The median duration of the completed runs, in milliseconds, or null when none completed. Read
/// from a histogram of durations whose bins are about 13% wide, so it is within one bin of the
/// exact value, and never outside the durations recorded.
/// </param>
/// <param name="P95DurationMs">The 95th percentile duration of the completed runs, as for <paramref name="P50DurationMs"/>.</param>
public sealed record MetricSeriesBucket(
    DateTime Start,
    int Completed,
    int Failed,
    int Cancelled,
    double? P50DurationMs,
    double? P95DurationMs
);
