using Trax.Scheduler.Sandbox.Launcher.Backends;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>A backend whose every run fails, as podman does when the named runtime is not configured.</summary>
internal sealed class FailingBackend(ISandboxBackend inner) : ISandboxBackend
{
    public IReadOnlyList<string> CanaryProbes => inner.CanaryProbes;

    public bool LoopbackOnly => inner.LoopbackOnly;

    public Task<SandboxExecution> RunAsync(SandboxJob job, CancellationToken ct) =>
        Task.FromResult(SandboxExecution.Failure("default OCI runtime \"kata\" not found"));

    public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
}
