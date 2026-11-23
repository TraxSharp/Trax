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
