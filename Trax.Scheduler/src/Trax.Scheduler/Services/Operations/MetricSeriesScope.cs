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
