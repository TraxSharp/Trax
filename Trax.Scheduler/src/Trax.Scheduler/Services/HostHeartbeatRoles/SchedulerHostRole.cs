using Trax.Effect.Data.Services.HostHeartbeats;
using Trax.Scheduler.Configuration;
using HostRow = Trax.Effect.Models.HostHeartbeat.HostHeartbeat;

namespace Trax.Scheduler.Services.HostHeartbeatRoles;

/// <summary>
/// Reports on this host's heartbeat row whether it runs the manifest manager and the job dispatcher,
/// each as registered on this host and switched on in its configuration at the beat.
/// </summary>
/// <param name="configuration">The scheduler configuration, read at every beat.</param>
/// <param name="runsDispatcher">Whether this host registered the job dispatcher, which needs a database provider.</param>
internal sealed class SchedulerHostRole(SchedulerConfiguration configuration, bool runsDispatcher)
    : IHostHeartbeatRole
{
    public void Describe(HostRow row)
    {
        row.RunsManifestManager = configuration.ManifestManagerEnabled;
        row.RunsDispatcher = runsDispatcher && configuration.JobDispatcherEnabled;
    }
}
