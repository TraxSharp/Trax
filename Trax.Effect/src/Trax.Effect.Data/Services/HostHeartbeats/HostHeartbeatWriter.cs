using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Host;
using HostRow = Trax.Effect.Models.HostHeartbeat.HostHeartbeat;

namespace Trax.Effect.Data.Services.HostHeartbeats;

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

        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);

        var row = await context
            .HostHeartbeats.FirstOrDefaultAsync(
                h => h.InstanceId == host.HostInstanceId,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new HostRow
            {
                InstanceId = host.HostInstanceId,
                HostName = host.HostName,
                HostEnvironment = host.HostEnvironment,
                TraxVersion = TraxVersion,
            };
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
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.HostHeartbeats.RemoveRange(expired);

        await context.SaveChanges(cancellationToken).ConfigureAwait(false);
    }
}
