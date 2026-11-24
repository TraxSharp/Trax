using System.ComponentModel;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>What the launcher's startup canary asks a sandbox to try.</summary>
/// <param name="Probes">
/// <c>host:port</c> pairs the sandbox tries to open a TCP connection to: the metadata endpoints,
/// the cluster's API server, the launcher itself. Each must fail.
/// </param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#inside-the-sandbox">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SandboxCanaryRequest(IReadOnlyList<string> Probes);
