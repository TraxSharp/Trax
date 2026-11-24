namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>Starts one sandbox, waits for it under its limits, reads its output volume after it exits, and removes everything it made.</summary>
internal interface ISandboxBackend
{
    /// <summary>Runs <paramref name="job"/> to its end. Never throws for anything the sandbox did; a runtime or API failure is a <see cref="SandboxEnd.Failed"/> execution.</summary>
    Task<SandboxExecution> RunAsync(SandboxJob job, CancellationToken ct);

    /// <summary>Removes whatever an earlier launcher process left behind (a crash mid-run), before any new run starts.</summary>
    Task SweepAsync(CancellationToken ct);

    /// <summary>The host:port pairs the canary probes on this backend, on top of the configured ones.</summary>
    IReadOnlyList<string> CanaryProbes { get; }

    /// <summary>Whether a sandbox here should have no network interface but loopback (podman), or has one the network policy must close (Kubernetes).</summary>
    bool LoopbackOnly { get; }
}
