namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Shared service for high-level operations performed by both the dashboard UI and the
/// GraphQL <c>operations</c> namespace. Centralising the logic here keeps both surfaces
/// behaviourally identical: a queue/cancel/update from the React (or Blazor) dashboard
/// runs the same code path as the same call from the GraphQL API.
/// </summary>
public interface IOperationsService
{
    /// <summary>
    /// Validates the input against the registered train's input type and inserts a new
    /// <see cref="Effect.Models.WorkQueue.WorkQueue"/> row in the <c>Queued</c> state.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Id: newEntryId, Count: 1, ...)</c> on success;
    /// <c>OperationResult(false, ...)</c> with a populated <c>Message</c> for unknown
    /// trains, missing <c>TrainName</c>, or invalid <c>InputJson</c>.
    /// </returns>
    Task<OperationResult> QueueTrainAsync(QueueTrainInput input, CancellationToken ct);

    /// <summary>
    /// Transitions a queued work queue entry to <c>Cancelled</c>. Only entries currently
    /// in the <c>Queued</c> state are eligible. Entries that are already dispatched or
    /// already cancelled return a failure result without modifying the row.
    /// </summary>
    Task<OperationResult> CancelWorkQueueEntryAsync(long id, CancellationToken ct);

    /// <summary>
    /// Patches mutable settings on a manifest group (max active jobs, priority, enabled
    /// flag). Each field on <paramref name="input"/> is optional and "no change by default":
    /// only properties explicitly set on the input are written. <c>UpdatedAt</c> is bumped
    /// when at least one field changed.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Id: groupId, Count: N, ...)</c> where <c>N</c> is the number
    /// of fields written; <c>OperationResult(false, ...)</c> if the group does not exist.
    /// </returns>
    Task<OperationResult> UpdateManifestGroupAsync(
        long id,
        UpdateManifestGroupInput input,
        CancellationToken ct
    );

    /// <summary>
    /// Returns the 1-hop cross-group dependency neighborhood for a manifest group:
    /// every group that contains a manifest the focal group's manifests depend on
    /// (upstream), every group that contains a manifest depending on the focal group's
    /// manifests (downstream), and the focal group itself. Edges are directed
    /// parent → dependent.
    /// </summary>
    /// <returns>
    /// <c>null</c> if the group does not exist or contains no manifests with cross-group
    /// dependencies; otherwise a graph that always includes the focal group as a node.
    /// </returns>
    Task<ManifestGroupDependencyGraph?> GetManifestGroupDependencyGraphAsync(
        long groupId,
        CancellationToken ct
    );

    /// <summary>
    /// Returns the whole cross-group dependency graph: every manifest group as a node and every
    /// cross-group dependency (a manifest in one group depending on a manifest in another) as a
    /// directed parent → dependent edge. Nothing is highlighted. Backs the global dependency
    /// graph on the dashboard's manifest-groups page.
    /// </summary>
    Task<ManifestGroupDependencyGraph> GetGlobalManifestGroupGraphAsync(CancellationToken ct);

    /// <summary>
    /// Returns a snapshot of dashboard-relevant metrics: today's KPI counts, an
    /// executions-over-time chart at the chosen granularity, top failing trains over
    /// the last 7 days, top average durations over the last 7 days, and per-train
    /// throughput sparklines over the last 7 days (28 6-hour buckets).
    /// </summary>
    /// <param name="range">Granularity of the executions-over-time chart only.</param>
    /// <param name="hideAdminTrains">
    /// When true, framework admin trains (matching <c>AdminTrains.FullNames</c>) are
    /// excluded from every series. Mirrors the dashboard's "Hide admin trains" toggle.
    /// </param>
    Task<DashboardMetrics> GetDashboardMetricsAsync(
        MetricsRange range,
        bool hideAdminTrains,
        CancellationToken ct
    );

    /// <summary>
    /// Returns a snapshot of host-process health: working set, GC heap, uptime, and
    /// process start time. Synchronous because all data comes from
    /// <see cref="System.Diagnostics.Process"/>.
    /// </summary>
    ServerMetrics GetServerMetrics();

    /// <summary>
    /// Returns the live scheduler runtime settings, reading from the in-memory
    /// <c>SchedulerConfiguration</c> singleton (and <c>LocalWorkerOptions</c> /
    /// <c>MetadataCleanupConfiguration</c> if registered). The singleton is the
    /// source of truth at runtime; the persisted row is loaded into it at startup
    /// by <c>SchedulerConfigBootstrapHostedService</c>.
    /// </summary>
    SchedulerConfigSnapshot GetSchedulerConfig();

    /// <summary>
    /// Patches the live scheduler runtime settings. Writes are applied to both the
    /// in-memory singleton (so changes take effect immediately) and to the persisted
    /// <c>trax.scheduler_config</c> row (so changes survive restart).
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Count: N, ...)</c> where <c>N</c> is the number of
    /// fields actually changed.
    /// </returns>
    Task<OperationResult> UpdateSchedulerConfigAsync(
        UpdateSchedulerConfigInput input,
        CancellationToken ct
    );
}
