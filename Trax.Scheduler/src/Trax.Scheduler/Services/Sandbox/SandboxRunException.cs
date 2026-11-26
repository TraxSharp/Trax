using Trax.Core.Exceptions;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// A sandboxed run did not complete: the launcher refused it, was busy, killed it, or refused its
/// output, or its answer could not be trusted. The message says which, and why. A train that ran
/// in the sandbox and failed there throws the failure it recorded instead.
/// </summary>
/// <remarks>
/// Thrown from a hub junction, it fails the hub's run with the reason recorded on it. Its message
/// is the launcher's and the hub's, not a train author's, so it is not shown to a client.
/// </remarks>
/// <param name="outcome">How the run ended.</param>
/// <param name="message">Why.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
public sealed class SandboxRunException(SandboxOutcome outcome, string message)
    : TrainException(message)
{
    /// <summary>How the run ended, as the launcher said or the hub decided.</summary>
    public SandboxOutcome Outcome { get; } = outcome;
}
