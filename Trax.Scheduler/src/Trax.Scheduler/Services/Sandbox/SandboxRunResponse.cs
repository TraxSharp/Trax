using System.ComponentModel;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// The launcher's answer to a <see cref="SandboxRunRequest"/>. Every answer, a refusal included,
/// is signed over its exact body and bound to the request it answers.
/// </summary>
/// <param name="RunId">The request's run id.</param>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Reason">Why it did not complete, in words an operator can act on; null when it completed.</param>
/// <param name="ResultJson">
/// With <see cref="SandboxOutcome.Completed"/>, the sandbox's own result: a
/// <c>RemoteRunResponse</c> as JSON, written by code in the sandbox and so untrusted.
/// </param>
/// <param name="Archive">
/// With <see cref="SandboxOutcome.Completed"/>, the files the train left in its output directory,
/// as a base64 tar the launcher wrote itself from entries it checked.
/// </param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#the-wire">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SandboxRunResponse(
    string RunId,
    SandboxOutcome Outcome,
    string? Reason = null,
    string? ResultJson = null,
    string? Archive = null
);
