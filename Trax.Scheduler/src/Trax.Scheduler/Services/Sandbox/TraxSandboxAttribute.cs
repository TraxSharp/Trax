namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// Marks a train as one that runs in a sandbox of its own: a fresh, short-lived microVM per run,
/// started by the sandbox launcher, with no network and no credentials. A sandbox runs only trains
/// that carry it, and a hub can send only these to a launcher.
/// </summary>
/// <remarks>
/// Every limit is declared, none has a default: what the work needs is the train author's to say.
/// The launcher refuses a run whose limits are over its host-wide caps, and kills one that runs
/// over its memory or its timeout. Put it on the train's class, not its interface.
/// </remarks>
/// <param name="profile">
/// The launcher profile to run in, which names the image (pinned by digest in the launcher's own
/// configuration; a request cannot name one).
/// </param>
/// <param name="cpuMillicores">CPU, in thousandths of a core.</param>
/// <param name="memoryMiB">Memory, in MiB.</param>
/// <param name="diskMiB">Output volume and scratch directory size, in MiB each.</param>
/// <param name="timeoutSeconds">Wall-clock time, in seconds.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/attributes/trax-sandbox">SDK reference</seealso>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TraxSandboxAttribute(
    string profile,
    int cpuMillicores,
    int memoryMiB,
    int diskMiB,
    int timeoutSeconds
) : Attribute
{
    /// <summary>The launcher profile the train runs in.</summary>
    public string Profile { get; } = profile;

    /// <summary>CPU, in thousandths of a core.</summary>
    public int CpuMillicores { get; } = cpuMillicores;

    /// <summary>Memory, in MiB.</summary>
    public int MemoryMiB { get; } = memoryMiB;

    /// <summary>Output volume and scratch directory size, in MiB each.</summary>
    public int DiskMiB { get; } = diskMiB;

    /// <summary>Wall-clock time, in seconds.</summary>
    public int TimeoutSeconds { get; } = timeoutSeconds;

    /// <summary>The limits as the launcher reads them.</summary>
    public SandboxLimits Limits => new(CpuMillicores, MemoryMiB, DiskMiB, TimeoutSeconds);
}
