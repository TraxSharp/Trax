using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>
/// Proves, before the launcher takes any request, that a sandbox really is isolated: it starts one
/// under the configured runtime and has it report its kernel and what it can reach. A sandbox on
/// this node's own kernel means the runtime is not a VM (a RuntimeClass named <c>kata</c> can map
/// to runc), and a sandbox that reaches any probe, or has a network interface where it should
/// have none, means the network is not closed. Either refuses the start. See scheduler/0022.
/// </summary>
internal sealed class RuntimeCanary(
    ISandboxBackend backend,
    SandboxLauncherOptions options,
    Func<CancellationToken, Task<string>> hostKernel,
    ILogger<RuntimeCanary> logger
)
{
    /// <summary>The cloud metadata endpoints, which every canary probes.</summary>
    internal static readonly string[] MetadataProbes =
    [
        "169.254.169.254:80",
        "169.254.170.23:80",
        "169.254.170.2:80",
        "[fd00:ec2::254]:80",
    ];

    /// <summary>Addresses off the host, to show there is no route out at all.</summary>
    internal static readonly string[] OutsideProbes = ["1.1.1.1:443", "8.8.8.8:53"];

    /// <summary>Reads this machine's kernel as the launcher sees it.</summary>
    internal static async Task<string> ReadHostKernelAsync(CancellationToken ct) =>
        (await File.ReadAllTextAsync("/proc/version", ct).ConfigureAwait(false)).Trim();

    public async Task CheckAsync(CancellationToken ct)
    {
        var profile = options.Profiles.OrderBy(p => p.Key, StringComparer.Ordinal).First();
        var probes = MetadataProbes
            .Concat(OutsideProbes)
            .Concat(backend.CanaryProbes)
            .Concat(options.CanaryProbes)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var job = new SandboxJob(
            Convert.ToHexStringLower(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)
            ),
            profile.Value,
            new SandboxLimits(500, 256, 16, 120),
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [SandboxPaths.CanaryFile] = JsonSerializer.SerializeToUtf8Bytes(
                    new SandboxCanaryRequest(probes),
                    SandboxJson.Options
                ),
            }
        );

        var execution = await backend.RunAsync(job, ct).ConfigureAwait(false);
        var report = Report(execution);
        var host = await hostKernel(ct).ConfigureAwait(false);
        Judge(report, host, backend.LoopbackOnly, options);
        logger.LogInformation(
            "Sandbox canary passed under {Runtime}: guest kernel '{Guest}', interfaces {Interfaces}, none of {Probes} probes reached.",
            options.Runtime,
            report.Kernel,
            string.Join(',', report.Interfaces),
            probes.Count
        );
    }

    private static SandboxCanaryReport Report(SandboxExecution execution)
    {
        if (execution.End != SandboxEnd.Exited || execution.ExitCode != 0)
            throw new SandboxRuntimeRefusedException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The sandbox canary did not run to completion ({execution.End}, exit {execution.ExitCode}): "
                )
                    + (execution.Detail ?? execution.OutputRefused ?? "no detail")
                    + ". The runtime may be missing or misconfigured; the launcher does not start sandboxes without it."
            );

        var file =
            execution.Output.FirstOrDefault(f =>
                string.Equals(f.Path, SandboxPaths.CanaryReportFile, StringComparison.Ordinal)
            )
            ?? throw new SandboxRuntimeRefusedException(
                "The sandbox canary left no report. Is the profile's image a Trax sandbox image?"
            );
        try
        {
            return JsonSerializer.Deserialize<SandboxCanaryReport>(
                    file.Content.Span,
                    SandboxJson.Options
                ) ?? throw new JsonException("null");
        }
        catch (JsonException ex)
        {
            throw new SandboxRuntimeRefusedException(
                "The sandbox canary's report is not readable.",
                ex
            );
        }
    }

    /// <summary>Throws when the report shows a sandbox that is not isolated.</summary>
    internal static void Judge(
        SandboxCanaryReport report,
        string hostKernel,
        bool loopbackOnly,
        SandboxLauncherOptions options
    )
    {
        if (
            string.IsNullOrWhiteSpace(report.Kernel)
            || string.Equals(report.Kernel.Trim(), hostKernel.Trim(), StringComparison.Ordinal)
        )
            throw new SandboxRuntimeRefusedException(
                $"The sandbox canary ran on this node's own kernel ('{hostKernel}'), so the runtime "
                    + $"configured as {options.Runtime} is not isolating it (a runtime class or podman "
                    + "runtime of that name may map to runc or crun). The launcher does not start sandboxes."
            );

        if (report.Reached.Count > 0)
            throw new SandboxRuntimeRefusedException(
                $"The sandbox canary reached {string.Join(", ", report.Reached)}. A sandbox must reach nothing; "
                    + "check the namespace's NetworkPolicy and that the cluster's network plugin enforces it."
            );

        var others = report
            .Interfaces.Where(i => !string.Equals(i, "lo", StringComparison.Ordinal))
            .ToList();
        if (loopbackOnly && others.Count > 0)
            throw new SandboxRuntimeRefusedException(
                $"The sandbox canary has network interfaces besides loopback ({string.Join(", ", others)})."
            );
    }
}
