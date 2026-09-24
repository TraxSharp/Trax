namespace Trax.Mediator.Services.TrustedExecution;

/// <summary>
/// Marks a region of code as "trusted infrastructure" for the purpose of
/// train authorization. When a trusted scope is active, <c>ITrainAuthorizationService</c>
/// skips per-train authorization checks. Used by scheduler pipelines and remote
/// job runners, whose work was already authorized at the original API submission, and by the
/// dashboard, the admin surface its host gates as a whole (docs/0017).
/// </summary>
/// <remarks>
/// Do not use this from request-handling code that serves callers a train's own authorization
/// is meant to check. It bypasses every <c>[TraxAuthorize]</c> check for as long as the returned
/// <see cref="IDisposable"/> is alive. The scope
/// is AsyncLocal so it flows across <c>await</c> boundaries within the same logical
/// call stack and does not leak across unrelated requests.
/// </remarks>
public interface ITrustedExecutionScope
{
    /// <summary>
    /// True when a trusted scope is currently active on this async flow.
    /// </summary>
    bool IsTrusted { get; }

    /// <summary>
    /// The reason passed to the innermost active <see cref="BeginTrusted"/> call,
    /// or <c>null</c> when no scope is active. Used for diagnostic logs only.
    /// </summary>
    string? CurrentReason { get; }

    /// <summary>
    /// Opens a trusted scope. Dispose the returned handle to close it.
    /// Scopes nest; inner scope wins for <see cref="CurrentReason"/>.
    /// </summary>
    /// <param name="reason">
    /// Short identifier for the bypass (e.g. <c>"scheduler.local-worker"</c>).
    /// Surfaces in diagnostic logs when a train is executed under trust.
    /// </param>
    IDisposable BeginTrusted(string reason);
}
