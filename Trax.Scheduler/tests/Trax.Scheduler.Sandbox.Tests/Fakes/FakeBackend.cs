using System.Text.Json;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>A backend that runs nothing: it answers each job as the test says, and the canary with a passing report unless told otherwise.</summary>
internal sealed class FakeBackend : ISandboxBackend
{
    public List<SandboxJob> Jobs { get; } = [];

    public Func<SandboxJob, Task<SandboxExecution>> Run { get; set; } =
        _ =>
            Task.FromResult(
                new SandboxExecution(
                    SandboxEnd.Exited,
                    0,
                    [new SandboxFile(SandboxPaths.ResultFile, "{\"metadataId\":0}"u8.ToArray())],
                    OutputRefused: null
                )
            );

    public SandboxCanaryReport Canary { get; set; } = new("Linux version guest", ["lo"], []);

    public int Sweeps { get; private set; }

    public IReadOnlyList<string> CanaryProbes => ["10.0.0.1:443"];

    public bool LoopbackOnly { get; set; } = true;

    public async Task<SandboxExecution> RunAsync(SandboxJob job, CancellationToken ct)
    {
        if (job.Input.ContainsKey(SandboxPaths.CanaryFile))
            return new SandboxExecution(
                SandboxEnd.Exited,
                0,
                [
                    new SandboxFile(
                        SandboxPaths.CanaryReportFile,
                        JsonSerializer.SerializeToUtf8Bytes(Canary, SandboxJson.Options)
                    ),
                ],
                OutputRefused: null
            );
        lock (Jobs)
            Jobs.Add(job);
        return await Run(job).ConfigureAwait(false);
    }

    public Task SweepAsync(CancellationToken ct)
    {
        Sweeps++;
        return Task.CompletedTask;
    }
}
