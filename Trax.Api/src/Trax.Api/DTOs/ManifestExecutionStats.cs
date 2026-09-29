namespace Trax.Api.DTOs;

/// <summary>
/// Execution roll-up for a single manifest: total run count broken down by state, plus the most
/// recent run and most recent successful run. Backs the summary cards on the dashboard's manifest
/// detail page.
/// </summary>
/// <param name="ManifestId">The manifest the counts are for.</param>
/// <param name="Total">All of the manifest's executions, in any state.</param>
/// <param name="Completed">Executions that completed.</param>
/// <param name="Failed">Executions that failed.</param>
/// <param name="InProgress">Executions running now.</param>
/// <param name="Pending">Executions created but not yet started.</param>
/// <param name="Cancelled">Executions that were cancelled.</param>
/// <param name="LastRun">When the most recent execution started (UTC), or <c>null</c> if it never ran.</param>
/// <param name="LastSuccessfulRun">When the most recent completed execution ended (UTC), or <c>null</c> if none completed.</param>
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
