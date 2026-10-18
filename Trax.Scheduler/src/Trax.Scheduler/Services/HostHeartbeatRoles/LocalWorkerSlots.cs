using Trax.Effect.Data.Services.HostHeartbeats;
using Trax.Scheduler.Configuration;
using HostRow = Trax.Effect.Models.HostHeartbeat.HostHeartbeat;

namespace Trax.Scheduler.Services.HostHeartbeatRoles;

/// <summary>
/// Counts this host's local workers that are running a job, and reports the pool's size and that
/// count on its heartbeat row.
/// </summary>
internal sealed class LocalWorkerSlots(LocalWorkerOptions options) : IHostHeartbeatRole
{
    private int _inUse;

    /// <summary>How many workers are running a job now.</summary>
    public int InUse => Volatile.Read(ref _inUse);

    /// <summary>Marks a worker busy until the returned handle is disposed.</summary>
    public IDisposable Take()
    {
        Interlocked.Increment(ref _inUse);
        return new Slot(this);
    }

    public void Describe(HostRow row)
    {
        row.WorkerSlots = options.WorkerCount;
        row.WorkerSlotsInUse = InUse;
    }

    private sealed class Slot(LocalWorkerSlots slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Interlocked.Decrement(ref slots._inUse);
        }
    }
}
