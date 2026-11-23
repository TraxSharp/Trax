namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Process-level health snapshot. Memory/GC/uptime are exact at call time; CPU%
/// requires consumer-side sampling state and is intentionally omitted from the shared
/// service.
/// </summary>
/// <param name="ProcessStartTimeUtc">When the host process started.</param>
/// <param name="UptimeSeconds">Seconds since process start.</param>
/// <param name="WorkingSetBytes">Resident set size (Process.WorkingSet64).</param>
/// <param name="GcHeapBytes">Total GC-managed heap (GC.GetTotalMemory(false)).</param>
public record ServerMetrics(
    DateTime ProcessStartTimeUtc,
    double UptimeSeconds,
    long WorkingSetBytes,
    long GcHeapBytes
);
