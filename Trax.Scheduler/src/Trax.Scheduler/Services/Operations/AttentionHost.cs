namespace Trax.Scheduler.Services.Operations;

/// <summary>A host that has missed three heartbeats in a row.</summary>
/// <param name="InstanceId">The process's identity, as stamped on its runs.</param>
/// <param name="HostName">The machine it ran on.</param>
/// <param name="LastHeartbeatAt">When it last beat (UTC).</param>
/// <param name="RunsDispatcher">Whether it ran the job dispatcher at its last beat.</param>
/// <param name="Link">The console's hosts.</param>
public sealed record AttentionHost(
    string InstanceId,
    string HostName,
    DateTime LastHeartbeatAt,
    bool RunsDispatcher,
    string Link
);
