using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Host;
using HostRow = Trax.Effect.Models.HostHeartbeat.HostHeartbeat;

namespace Trax.Effect.Data.Services.HostHeartbeats;

/// <summary>
/// Something running in this process that a host's heartbeat row reports: the scheduler's
/// dispatcher and manifest manager, and the local worker pool. Each sets its own columns on the row
/// at every beat. Registered by the package that runs it; a process with none reports none.
/// </summary>
internal interface IHostHeartbeatRole
{
    void Describe(HostRow row);
}

/// <summary>
/// Writes this process's row in <c>trax.host_heartbeat</c> and deletes the rows of hosts that last
/// beat <see cref="HostRow.Retention"/> ago, over a data context of its own for each beat so it
/// saves nothing else. <see cref="HostHeartbeatService"/> calls <see cref="Beat"/> every
/// <see cref="HostRow.Interval"/>; a test calls it directly with the clock it controls.
/// </summary>
internal sealed class HostHeartbeatWriter(
    IDataContextProviderFactory contexts,
    TimeProvider clock,
    IEnumerable<IHostHeartbeatRole> roles
)
{
    internal static readonly string TraxVersion =
        typeof(HostHeartbeatWriter)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    private readonly IHostHeartbeatRole[] _roles = roles.ToArray();
    private DateTime? _startedAt;

    /// <summary>
    /// Upserts this process's row with the current time and roles, then prunes the expired rows.
    /// The identity is <see cref="TraxHostInfo.Current"/>, the one stamped on each run, read at every
    /// beat.
    /// </summary>
    public async Task Beat(CancellationToken cancellationToken)
    {
        var host = TraxHostInfo.Current ?? TraxHostInfo.AutoDetect();
        var now = clock.GetUtcNow().UtcDateTime;
        _startedAt ??= now;

        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        var row = await context.HostHeartbeats.FirstOrDefaultAsync(
            h => h.InstanceId == host.HostInstanceId,
            cancellationToken
        );
        if (row is null)
        {
            row = new HostRow { InstanceId = host.HostInstanceId };
            context.HostHeartbeats.Add(row);
        }

        row.HostName = host.HostName;
        row.HostEnvironment = host.HostEnvironment;
        row.TraxVersion = TraxVersion;
        row.StartedAt = _startedAt.Value;
        row.LastHeartbeatAt = now;
        row.RunsDispatcher = false;
        row.RunsManifestManager = false;
        row.WorkerSlots = null;
        row.WorkerSlotsInUse = null;
        foreach (var role in _roles)
            role.Describe(row);

        // Loaded and removed rather than ExecuteDelete, which the InMemory provider cannot run. At
        // one row per process seen in a day, each beat finds a handful at most.
        var expiredBefore = now - HostRow.Retention;
        var expired = await context
            .HostHeartbeats.Where(h =>
                h.LastHeartbeatAt < expiredBefore && h.InstanceId != host.HostInstanceId
            )
            .ToListAsync(cancellationToken);
        context.HostHeartbeats.RemoveRange(expired);

        await context.SaveChanges(cancellationToken);
    }
}
