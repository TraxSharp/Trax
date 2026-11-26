namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>
/// The host-wide caps the launcher never goes over. A request over one is refused, not lowered,
/// so a train finds out its declaration does not fit rather than running short of what it said.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher">SDK reference</seealso>
public sealed class SandboxCaps
{
    /// <summary>The most CPU one sandbox may declare, in millicores. 4000 by default.</summary>
    public int MaxCpuMillicores { get; set; } = 4000;

    /// <summary>The most memory one sandbox may declare, in MiB. 4096 by default.</summary>
    public int MaxMemoryMiB { get; set; } = 4096;

    /// <summary>The most disk one sandbox may declare, in MiB. 2048 by default.</summary>
    public int MaxDiskMiB { get; set; } = 2048;

    /// <summary>The longest timeout one sandbox may declare, in seconds. 3600 by default.</summary>
    public int MaxTimeoutSeconds { get; set; } = 3600;

    /// <summary>How many sandboxes may run at once. A request past it is refused as busy, not queued. 4 by default.</summary>
    public int MaxConcurrent { get; set; } = 4;

    /// <summary>The most processes and threads in one sandbox, which stops a fork bomb. 512 by default.</summary>
    public int MaxPids { get; set; } = 512;

    /// <summary>The largest input, in bytes of UTF-8 JSON. 4 MiB by default.</summary>
    public int MaxInputBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>The largest result file a sandbox may leave, in bytes. 16 MiB by default.</summary>
    public int MaxResultBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>The most file bytes a sandbox may hand back, uncompressed. 64 MiB by default.</summary>
    public long MaxArchiveBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>The most files and directories a sandbox may hand back. 10000 by default.</summary>
    public int MaxArchiveFiles { get; set; } = 10_000;

    internal string? Invalid() =>
        MaxCpuMillicores <= 0 || MaxMemoryMiB <= 0 || MaxDiskMiB <= 0 || MaxTimeoutSeconds <= 0
            ? "every cap must be positive"
        : MaxConcurrent <= 0 ? "MaxConcurrent must be positive"
        : MaxPids < 16 ? "MaxPids must be at least 16"
        : MaxInputBytes <= 0 || MaxResultBytes <= 0 || MaxArchiveBytes <= 0 || MaxArchiveFiles <= 0
            ? "every size cap must be positive"
        : null;
}
