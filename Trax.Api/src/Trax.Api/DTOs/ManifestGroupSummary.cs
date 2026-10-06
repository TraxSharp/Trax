namespace Trax.Api.DTOs;

/// <summary>
/// A manifest group: a named set of manifests that share a concurrency limit, a dispatch
/// priority and an enabled switch.
/// </summary>
/// <param name="Id">The group's id.</param>
/// <param name="Name">The group's unique name.</param>
/// <param name="MaxActiveJobs">The most executions from this group that may run at once, or <c>null</c> for no per-group limit (only the global limit applies).</param>
/// <param name="Priority">The group's dispatch priority, 0 to 31; work from a higher-priority group is dispatched first.</param>
/// <param name="IsEnabled">Whether the group's manifests are eligible to be queued and dispatched.</param>
/// <param name="CreatedAt">When the group was created (UTC).</param>
/// <param name="UpdatedAt">When the group was last changed (UTC).</param>
public record ManifestGroupSummary(
    long Id,
    string Name,
    int? MaxActiveJobs,
    int Priority,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
