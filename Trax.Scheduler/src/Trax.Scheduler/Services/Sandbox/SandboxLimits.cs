using System.ComponentModel;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// What one sandboxed run may use: CPU, memory, disk and wall-clock time. A train declares them
/// with <see cref="TraxSandboxAttribute"/>; the launcher refuses a request over its own host-wide
/// caps rather than lowering it.
/// </summary>
/// <param name="CpuMillicores">CPU, in thousandths of a core (1000 is one core).</param>
/// <param name="MemoryMiB">Memory, in MiB. The sandbox is killed when it goes over.</param>
/// <param name="DiskMiB">
/// The size of the sandbox's output volume and of its scratch directory, in MiB each. A write past
/// it fails with no space left.
/// </param>
/// <param name="TimeoutSeconds">Wall-clock time from start, in seconds. The sandbox is killed when it runs over.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SandboxLimits(
    int CpuMillicores,
    int MemoryMiB,
    int DiskMiB,
    int TimeoutSeconds
)
{
    /// <summary>
    /// Why these limits cannot be run anywhere, or null when each is positive.
    /// </summary>
    public string? Invalid() =>
        CpuMillicores <= 0 ? "CpuMillicores must be positive."
        : MemoryMiB <= 0 ? "MemoryMiB must be positive."
        : DiskMiB <= 0 ? "DiskMiB must be positive."
        : TimeoutSeconds <= 0 ? "TimeoutSeconds must be positive."
        : null;
}
