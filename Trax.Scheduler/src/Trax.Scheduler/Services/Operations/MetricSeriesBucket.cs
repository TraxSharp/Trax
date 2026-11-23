namespace Trax.Scheduler.Services.Operations;

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
