using System.Text.Json;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>Remembers the probes the canary asked for.</summary>
internal sealed class RecordingCanaryBackend(ISandboxBackend inner) : ISandboxBackend
{
    public List<string> Probes { get; } = [];

    public IReadOnlyList<string> CanaryProbes => inner.CanaryProbes;

    public bool LoopbackOnly => inner.LoopbackOnly;

    public Task<SandboxExecution> RunAsync(SandboxJob job, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<SandboxCanaryRequest>(
            job.Input[SandboxPaths.CanaryFile],
            SandboxJson.Options
        );
        Probes.AddRange(request?.Probes ?? []);
        return inner.RunAsync(job, ct);
    }

    public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
}
