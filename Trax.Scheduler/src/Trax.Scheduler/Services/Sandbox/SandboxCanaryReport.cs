using System.ComponentModel;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>What a sandbox reports to the launcher's startup canary.</summary>
/// <param name="Kernel">The contents of <c>/proc/version</c> as the sandbox sees it. Under a microVM it is the guest's kernel, which the launcher compares with its own node's.</param>
/// <param name="Interfaces">The network interfaces the sandbox has.</param>
/// <param name="Reached">The probes it could open a connection to; empty when none.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#inside-the-sandbox">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SandboxCanaryReport(
    string Kernel,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> Reached
);
