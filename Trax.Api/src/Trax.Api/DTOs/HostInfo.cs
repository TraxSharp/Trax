namespace Trax.Api.DTOs;

/// <summary>
/// A process (host instance) that has executed trains, rolled up from the metadata table by
/// <c>HostInstanceId</c>. Backs the dashboard's cluster view, which answers "which processes are
/// running work, and are they still alive?" across a split API + scheduler + worker deployment.
/// </summary>
/// <param name="InstanceId">Stable per-process id (survives for the lifetime of the process).</param>
/// <param name="Name">Machine / container host name, if stamped.</param>
/// <param name="Environment">Hosting environment (e.g. Production, Development), if stamped.</param>
/// <param name="LastSeen">Most recent execution start on this host. The freshness signal.</param>
/// <param name="TotalExecutions">Total executions attributed to this host.</param>
/// <param name="CurrentlyRunning">Executions on this host still in <c>InProgress</c>.</param>
public record HostInfo(
    string InstanceId,
    string? Name,
    string? Environment,
    DateTime LastSeen,
    long TotalExecutions,
    int CurrentlyRunning
);
