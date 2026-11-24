using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>What a backend reports of a finished sandbox, every resource it made already removed.</summary>
/// <param name="End">How the process ended.</param>
/// <param name="ExitCode">Its exit code, when it exited.</param>
/// <param name="Output">The output directory's files, checked; paths relative to it.</param>
/// <param name="OutputRefused">Why the output was refused, when it was; <paramref name="Output"/> is then empty.</param>
/// <param name="Detail">What went wrong, for <see cref="SandboxEnd.Failed"/>.</param>
internal sealed record SandboxExecution(
    SandboxEnd End,
    int? ExitCode,
    IReadOnlyList<SandboxFile> Output,
    string? OutputRefused,
    string? Detail = null
)
{
    public static SandboxExecution Failure(string detail) =>
        new(SandboxEnd.Failed, ExitCode: null, Output: [], OutputRefused: null, detail);
}
