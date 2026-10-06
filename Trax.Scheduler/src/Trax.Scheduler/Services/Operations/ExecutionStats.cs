namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Run counts for one manifest, by state, with its most recent run and most recent successful
/// run. Returned by <see cref="IOperationsService.GetManifestExecutionStatsAsync"/>; backs the
/// summary cards on a manifest's detail page.
/// </summary>
/// <param name="ManifestId">The manifest the counts are for.</param>
/// <param name="Total">Every run of the manifest, whatever its state.</param>
/// <param name="Completed">Runs in <c>Completed</c>.</param>
/// <param name="Failed">Runs in <c>Failed</c>.</param>
/// <param name="InProgress">Runs in <c>InProgress</c>.</param>
/// <param name="Pending">Runs in <c>Pending</c>.</param>
/// <param name="Cancelled">Runs in <c>Cancelled</c>.</param>
/// <param name="LastRun">The latest start time of any run, or null when there is none.</param>
/// <param name="LastSuccessfulRun">The latest end time of a completed run, or null.</param>
public record ManifestExecutionStats(
    long ManifestId,
    long Total,
    long Completed,
    long Failed,
    long InProgress,
    long Pending,
    long Cancelled,
    DateTime? LastRun,
    DateTime? LastSuccessfulRun
);

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
);
