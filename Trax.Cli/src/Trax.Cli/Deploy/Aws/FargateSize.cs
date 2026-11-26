using System.Globalization;

namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// A Fargate task's size: CPU in units (1024 is one vCPU) and memory in MiB. Fargate accepts only certain pairs, so
/// a service's <c>memory</c> and <c>cpus</c> become the smallest pair that holds both.
/// </summary>
/// <param name="Cpu">The task's CPU units.</param>
/// <param name="MemoryMiB">The task's memory, in MiB.</param>
internal sealed record FargateSize(int Cpu, int MemoryMiB)
{
    /// <summary>A task's size when its service names neither memory nor CPU: half a vCPU and 1 GiB.</summary>
    public static readonly FargateSize Default = new(512, 1024);

    // Per vCPU-hour and per GB-hour, us-east-1 Linux x86 on-demand, November 2026.
    private const decimal VcpuHour = 0.04048m;
    private const decimal GbHour = 0.004445m;
    private const decimal HoursAMonth = 730m;

    /// <summary>Every pair Fargate accepts, smallest first: each CPU size with the memory sizes it allows.</summary>
    internal static readonly IReadOnlyList<FargateSize> Valid =
    [
        .. new[] { 512, 1024, 2048 }.Select(m => new FargateSize(256, m)),
        .. Steps(1024, 4096, 1024).Select(m => new FargateSize(512, m)),
        .. Steps(2048, 8192, 1024).Select(m => new FargateSize(1024, m)),
        .. Steps(4096, 16384, 1024).Select(m => new FargateSize(2048, m)),
        .. Steps(8192, 30720, 1024).Select(m => new FargateSize(4096, m)),
        .. Steps(16384, 61440, 4096).Select(m => new FargateSize(8192, m)),
        .. Steps(32768, 122880, 8192).Select(m => new FargateSize(16384, m)),
    ];

    /// <summary>
    /// The smallest pair with at least <paramref name="memoryMiB"/> and <paramref name="cpus"/>, by cost. With
    /// neither, <see cref="Default"/>; with only one, the other is at least the default's, so naming memory never
    /// shrinks the CPU a task had before.
    /// </summary>
    public static FargateSize For(int? memoryMiB, decimal? cpus)
    {
        if (memoryMiB is null && cpus is null)
            return Default;
        var memory = memoryMiB ?? Default.MemoryMiB;
        var units = cpus is { } c ? (int)Math.Ceiling(c * 1024) : Default.Cpu;
        return Valid
                .Where(s => s.Cpu >= units && s.MemoryMiB >= memory)
                .OrderBy(s => s.Monthly)
                .ThenBy(s => s.Cpu)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"No Fargate task holds {memory} MiB and {units} CPU units; the largest is 16 vCPU and 120 GiB."
                )
            );
    }

    /// <summary>What one copy costs a month, running all of it.</summary>
    public decimal Monthly => (Cpu / 1024m * VcpuHour + MemoryMiB / 1024m * GbHour) * HoursAMonth;

    /// <summary>The size as plan prints it: <c>0.5 vCPU and 1 GB</c>.</summary>
    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Cpu / 1024m:0.##} vCPU and {MemoryMiB / 1024m:0.##} GB"
        );

    private static IEnumerable<int> Steps(int from, int to, int step)
    {
        for (var m = from; m <= to; m += step)
            yield return m;
    }
}
