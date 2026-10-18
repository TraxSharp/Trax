namespace Trax.Api.DTOs;

/// <summary>
/// A host process, from the heartbeat row it rewrites every 15 seconds in
/// <c>trax.host_heartbeat</c>. Backs the console's cluster view, which answers "which processes
/// are up, what do they run, and is any of them taking the work that waits?" across a split API,
/// scheduler and worker deployment.
/// </summary>
/// <param name="InstanceId">The process's instance id (a pod name, an ECS task id, or machine name and process id), the same id stamped on each run it executes.</param>
/// <param name="Name">The machine or container host name.</param>
/// <param name="Environment">The kind of environment the process runs in, such as <c>server</c>, <c>ecs</c>, <c>kubernetes</c> or <c>lambda</c>.</param>
/// <param name="TraxVersion">The Trax version the process runs.</param>
/// <param name="StartedAt">When the process began beating.</param>
/// <param name="LastHeartbeat">When the process last wrote its row.</param>
/// <param name="Online">Whether the last heartbeat is less than 45 seconds old, three beats. An offline host has stopped, hung, or lost its database.</param>
/// <param name="RunsDispatcher">Whether the process runs the job dispatcher, which moves queued work to workers, with it switched on.</param>
/// <param name="RunsManifestManager">Whether the process runs the manifest manager, which queues scheduled work, with it switched on.</param>
/// <param name="WorkerSlots">How many local workers the process runs, or null when it runs none.</param>
/// <param name="WorkerSlotsInUse">How many of those workers were running a job at the last heartbeat, or null when it runs none.</param>
/// <param name="CurrentlyRunning">Runs attributed to this process that are still <c>InProgress</c>.</param>
public record HostInfo(
    string InstanceId,
    string Name,
    string Environment,
    string TraxVersion,
    DateTime StartedAt,
    DateTime LastHeartbeat,
    bool Online,
    bool RunsDispatcher,
    bool RunsManifestManager,
    int? WorkerSlots,
    int? WorkerSlotsInUse,
    int CurrentlyRunning
);
