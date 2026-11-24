using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>One sandbox to run: built by the launcher from a checked request, never by a hub.</summary>
/// <param name="RunId">32 lowercase hex characters; names every resource the run creates.</param>
/// <param name="Profile">The profile, from the launcher's configuration.</param>
/// <param name="Limits">The limits, already checked against the caps.</param>
/// <param name="Input">The files to put in the input directory, by name.</param>
internal sealed record SandboxJob(
    string RunId,
    SandboxProfile Profile,
    SandboxLimits Limits,
    IReadOnlyDictionary<string, byte[]> Input
);
