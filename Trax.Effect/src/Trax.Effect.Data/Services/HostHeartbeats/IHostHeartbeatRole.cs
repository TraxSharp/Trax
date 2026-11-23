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
