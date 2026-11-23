namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Manifest and run counts for one manifest group, with its most recent run. Returned by
/// <see cref="IOperationsService.GetManifestGroupExecutionStatsAsync"/>; backs the per-group
/// columns on the manifest groups list.
/// </summary>
/// <param name="GroupId">The group the counts are for.</param>
/// <param name="ManifestCount">Manifests in the group.</param>
/// <param name="TotalExecutions">Runs of the group's manifests, whatever their state.</param>
/// <param name="Completed">Runs in <c>Completed</c>.</param>
/// <param name="Failed">Runs in <c>Failed</c>.</param>
/// <param name="InProgress">Runs in <c>InProgress</c>.</param>
/// <param name="LastRun">The latest start time of any of the group's runs, or null.</param>
public record ManifestGroupExecutionStats(
    long GroupId,
    long ManifestCount,
    long TotalExecutions,
    long Completed,
    long Failed,
    long InProgress,
    DateTime? LastRun
)
{
    /// <summary>
    /// Whether the group's latest ended run failed: of its runs that completed, failed or were
    /// cancelled, the one that started last. False when none has ended.
    /// </summary>
    public bool LastEndedRunFailed { get; init; }
}
