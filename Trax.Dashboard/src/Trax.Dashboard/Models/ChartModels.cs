namespace Trax.Dashboard.Models;

/// <summary>
/// One bucket of the home page's executions-over-time chart: how many runs ended in each
/// terminal state within the bucket. Chart row model for the dashboard's own home page; not
/// intended to be used directly.
/// </summary>
internal class ExecutionTimePoint
{
    /// <summary>
    /// The axis label: the hour (<c>HH</c>) for the 24-hour view, or <c>HH:mm</c> for the
    /// 60-minute view, where labels not on a five-minute mark carry a leading space so the axis
    /// hides them.
    /// </summary>
    public string Label { get; init; } = "";

    /// <summary>Runs in the bucket that completed.</summary>
    public int Completed { get; init; }

    /// <summary>Runs in the bucket that failed.</summary>
    public int Failed { get; init; }

    /// <summary>Runs in the bucket that were cancelled.</summary>
    public int Cancelled { get; init; }
}

/// <summary>
/// One row of the home page's top-failures panel. Chart row model for the dashboard's own home
/// page; not intended to be used directly.
/// </summary>
internal class TrainFailureCount
{
    /// <summary>The train's unqualified name (its full name after the last dot).</summary>
    public string Name { get; init; } = "";

    /// <summary>Failed runs of the train in the last 7 days.</summary>
    public int Count { get; init; }
}

/// <summary>
/// One row of the home page's average-duration panel. Chart row model for the dashboard's own
/// home page; not intended to be used directly.
/// </summary>
internal class TrainDuration
{
    /// <summary>The train's unqualified name (its full name after the last dot).</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Mean duration in milliseconds of the train's completed top-level runs (no parent run) in
    /// the last 7 days, rounded to a whole number.
    /// </summary>
    public double AvgMs { get; init; }
}

/// <summary>
/// One bucket of a <see cref="ThroughputSeries"/>. Chart row model for the dashboard's own home
/// page; not intended to be used directly.
/// </summary>
internal class ThroughputPoint
{
    /// <summary>
    /// The axis label: <c>MMM dd</c> at midnight, otherwise <c>MMM dd HH</c> with a leading space
    /// so the axis hides it.
    /// </summary>
    public string Label { get; init; } = "";

    /// <summary>Completed runs of the series' train, or trains for <c>Other</c>, in the six-hour bucket.</summary>
    public int Count { get; init; }
}

/// <summary>
/// One line of the home page's seven-day throughput sparkline: a single top train, or
/// <c>Other</c> for the rest combined. Chart row model for the dashboard's own home page; not
/// intended to be used directly.
/// </summary>
internal class ThroughputSeries
{
    /// <summary>The train's unqualified name, or <c>Other</c>.</summary>
    public string Name { get; init; } = "";

    /// <summary>The CSS hex colour the series is drawn in.</summary>
    public string Color { get; init; } = "";

    /// <summary>The series' six-hour buckets covering the last 7 days, oldest first.</summary>
    public List<ThroughputPoint> Points { get; init; } = [];
}
