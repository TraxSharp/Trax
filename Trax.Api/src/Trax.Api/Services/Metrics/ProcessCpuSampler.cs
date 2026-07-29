using System.Diagnostics;

namespace Trax.Api.Services.Metrics;

/// <summary>
/// Samples this process's CPU utilisation as a percentage of one logical core's worth of time,
/// normalised by core count (so 100% means all cores fully busy). CPU% can only be measured as a
/// delta between two points in time, so this holds the previous sample; the dashboard polls it on
/// an interval and each call reports usage since the last. Registered as a singleton so the delta
/// state survives across requests.
/// </summary>
/// <remarks>
/// This lives in the API layer, not the shared <c>IOperationsService</c>, precisely because it is
/// stateful per consumer: the scheduler and dashboard each need their own sampler. The first call
/// only primes the baseline and returns <c>null</c>.
/// </remarks>
public sealed class ProcessCpuSampler
{
    private readonly Func<(TimeSpan Cpu, DateTime At)> _read;
    private readonly int _cores;
    private readonly object _gate = new();
    private TimeSpan _prevCpu;
    private DateTime _prevAt;
    private bool _primed;

    public ProcessCpuSampler()
        : this(
            () =>
            {
                using var proc = Process.GetCurrentProcess();
                return (proc.TotalProcessorTime, DateTime.UtcNow);
            },
            Environment.ProcessorCount
        ) { }

    // Seam for deterministic tests: inject the CPU-time/clock reader and core count.
    internal ProcessCpuSampler(Func<(TimeSpan Cpu, DateTime At)> read, int cores)
    {
        _read = read;
        _cores = cores;
    }

    /// <summary>
    /// CPU usage since the previous sample, clamped to 0-100. Returns <c>null</c> on the first call
    /// (no interval to compare) or if no measurable time has elapsed since the last call.
    /// </summary>
    public double? SamplePercent()
    {
        var (cpu, at) = _read();
        lock (_gate)
        {
            if (!_primed)
            {
                _prevCpu = cpu;
                _prevAt = at;
                _primed = true;
                return null;
            }

            var result = ComputePercent(cpu - _prevCpu, (at - _prevAt).TotalMilliseconds, _cores);
            _prevCpu = cpu;
            _prevAt = at;
            return result;
        }
    }

    /// <summary>
    /// Pure CPU-percent calculation: consumed CPU time over wall-clock elapsed, per core, as a
    /// percentage clamped to 0-100. Returns <c>null</c> when the interval or core count is not
    /// positive (nothing measurable).
    /// </summary>
    internal static double? ComputePercent(TimeSpan cpuDelta, double elapsedMs, int cores)
    {
        if (elapsedMs <= 0 || cores <= 0)
            return null;

        var percent = cpuDelta.TotalMilliseconds / elapsedMs / cores * 100;
        return Math.Clamp(Math.Round(percent, 1), 0, 100);
    }
}
