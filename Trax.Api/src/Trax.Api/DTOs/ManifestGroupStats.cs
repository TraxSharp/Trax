namespace Trax.Api.DTOs;

/// <summary>
/// Execution roll-up for a single manifest group: how many manifests it holds and its executions
/// broken down by state, plus the group's most recent run. Backs the per-group stat columns on the
/// dashboard's manifest groups list. Fetched in a batch keyed by the visible page's group ids.
/// </summary>
public record ManifestGroupStats(
    long GroupId,
    long ManifestCount,
    long TotalExecutions,
    long Completed,
    long Failed,
    long InProgress,
    DateTime? LastRun
);
