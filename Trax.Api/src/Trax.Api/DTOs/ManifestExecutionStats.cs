namespace Trax.Api.DTOs;

/// <summary>
/// Execution roll-up for a single manifest: total run count broken down by state, plus the most
/// recent run and most recent successful run. Backs the summary cards on the dashboard's manifest
/// detail page. Served index-only by ix_metadata_manifest_state (migration 038).
/// </summary>
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
