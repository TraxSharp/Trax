using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.HostHeartbeat;

/// <summary>
/// Base model for <c>trax.host_heartbeat</c>: one row per running host process, rewritten by the
/// process every <see cref="Interval"/> whether or not it has work, so an idle host and a dead one
/// can be told apart.
/// </summary>
/// <remarks>
/// Every host with a data provider writes its own row, keyed by
/// <see cref="Host.TraxHostInfo.HostInstanceId"/>, the same identity stamped on each run's
/// metadata. A host is offline once its <see cref="LastHeartbeatAt"/> is
/// <see cref="OfflineAfter"/> old (three missed beats), and its row is deleted by any host's next
/// beat once it is <see cref="Retention"/> old.
///
/// EF Core mapping lives in <c>Trax.Effect.Data.Models.HostHeartbeat.PersistentHostHeartbeat</c>;
/// the table ships in the core migration set (Postgres <c>086</c>, Sqlite <c>045</c>).
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/host-tracking">SDK reference</seealso>
public class HostHeartbeat
{
    /// <summary>How often a host writes its row.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    /// <summary>How old a row's last beat is when its host counts as offline: three missed beats.</summary>
    internal static readonly TimeSpan OfflineAfter = Interval * 3;

    /// <summary>How old a row's last beat is when the next beat of any host deletes it.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    /// <summary>
    /// The process's instance id (<see cref="Host.TraxHostInfo.HostInstanceId"/>): a pod name, an ECS
    /// task id, or machine name and process id. The primary key.
    /// </summary>
    [Column("instance_id")]
    public string InstanceId { get; set; } = null!;

    /// <summary>The machine or container host name (<see cref="Host.TraxHostInfo.HostName"/>).</summary>
    [Column("host_name")]
    public string HostName { get; set; } = null!;

    /// <summary>
    /// The kind of environment the process runs in (<see cref="Host.TraxHostInfo.HostEnvironment"/>):
    /// <c>server</c>, <c>ecs</c>, <c>kubernetes</c>, <c>lambda</c>, <c>azure-app-service</c>, or the
    /// value given to <c>SetHostEnvironment</c>.
    /// </summary>
    [Column("host_environment")]
    public string HostEnvironment { get; set; } = null!;

    /// <summary>The Trax version the process runs, so a rolling upgrade shows which hosts it has reached.</summary>
    [Column("trax_version")]
    public string TraxVersion { get; set; } = null!;

    /// <summary>When the process began beating, in UTC. Rewritten when a restarted process reuses the instance id.</summary>
    [Column("started_at")]
    public DateTime StartedAt { get; set; }

    /// <summary>When the process last wrote its row, in UTC.</summary>
    [Column("last_heartbeat_at")]
    public DateTime LastHeartbeatAt { get; set; }

    /// <summary>
    /// Whether the process runs the scheduler's job dispatcher, which moves queued work to workers,
    /// and has it switched on.
    /// </summary>
    [Column("runs_dispatcher")]
    public bool RunsDispatcher { get; set; }

    /// <summary>
    /// Whether the process runs the scheduler's manifest manager, which queues scheduled work, and
    /// has it switched on.
    /// </summary>
    [Column("runs_manifest_manager")]
    public bool RunsManifestManager { get; set; }

    /// <summary>How many local workers the process runs, or null when it runs none.</summary>
    [Column("worker_slots")]
    public int? WorkerSlots { get; set; }

    /// <summary>How many of those workers were running a job at the last beat, or null when it runs none.</summary>
    [Column("worker_slots_in_use")]
    public int? WorkerSlotsInUse { get; set; }

    /// <summary>
    /// Whether a host whose last beat was at <paramref name="lastHeartbeatAt"/> is online at
    /// <paramref name="now"/>: fewer than <see cref="OfflineAfter"/> have passed. A stored time read
    /// back without a kind (SQLite) is taken as UTC, which is how it was written.
    /// </summary>
    internal static bool IsOnline(DateTime lastHeartbeatAt, DateTimeOffset now) =>
        now.UtcDateTime - DateTime.SpecifyKind(lastHeartbeatAt, DateTimeKind.Utc) < OfflineAfter;
}
