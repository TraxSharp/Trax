namespace Trax.Dashboard.Models;

/// <summary>
/// One row of the manifest groups list: a manifest group's settings plus execution counts across
/// all of its manifests. Row model for the dashboard's own manifest groups page; not intended to be
/// used directly.
/// </summary>
internal class GroupSummary
{
    /// <summary>The manifest group's database id.</summary>
    public long Id { get; init; }

    /// <summary>The manifest group's name.</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// The most runs of this group's manifests allowed to be active at once, or
    /// <see langword="null"/> when the group sets no limit of its own and only the scheduler-wide
    /// limit applies.
    /// </summary>
    public int? MaxActiveJobs { get; init; }

    /// <summary>The group's dispatch priority (0 to 31); higher-priority groups have their work queue entries dispatched first.</summary>
    public int Priority { get; init; }

    /// <summary>Whether the group's manifests are eligible for dispatch. When <see langword="false"/>, none of them is queued or dispatched.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Number of manifests in the group.</summary>
    public int ManifestCount { get; init; }

    /// <summary>Number of runs (metadata rows) of the group's manifests, in any state, over all time.</summary>
    public int TotalExecutions { get; init; }

    /// <summary>Number of those runs that completed.</summary>
    public int CompletedCount { get; init; }

    /// <summary>Number of those runs that failed.</summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Start time of the group's most recent run, as stored (UTC), or <see langword="null"/> when
    /// no manifest in the group has run.
    /// </summary>
    public DateTime? LastRun { get; init; }
}
