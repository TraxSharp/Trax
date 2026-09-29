namespace Trax.Mediator.Services.TrustedExecution;

/// <summary>
/// Marks the current async flow as trusted infrastructure, which switches off train
/// authorization for every train run or queued on that flow until the scope is disposed.
/// Resolve it from DI (<c>AddMediator</c> registers it as a singleton) and open it with
/// <see cref="BeginTrusted"/> in a <c>using</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What trusting does.</strong> Trust bypasses the fail-closed authorization that
/// <c>[TraxAuthorize]</c> otherwise guarantees. Inside a scope:
/// </para>
/// <list type="bullet">
/// <item>
/// Trax.Api's <c>TrainAuthorizationService</c> returns without checking anything: no request,
/// authenticated user, policy or role is required. It logs the skip at Information level with
/// <see cref="CurrentReason"/>.
/// </item>
/// <item>
/// On a host with no <c>ITrainAuthorizationService</c>, a <c>[TraxAuthorize]</c> train is run or
/// queued instead of being refused with <c>TrainAuthorizationNotConfiguredException</c>.
/// </item>
/// </list>
/// <para>
/// A custom <c>ITrainAuthorizationService</c> is still called inside a trusted scope. It must check
/// <see cref="IsTrusted"/> itself if it wants the same behaviour. Nothing else changes under
/// trust: train lookup, the input size cap, input validation, concurrency limits and queue hooks
/// all apply as usual.
/// </para>
/// <para>
/// <strong>When to open one.</strong> Only around work that was already authorized at a gate of
/// its own. Trax opens scopes for a scheduler dispatching queued work, a remote worker running a job
/// it was sent, and the dashboard, whose host gates the whole admin surface. Never open one around
/// code that serves a caller the train's authorization is meant to check, and never decide to open
/// one from anything the caller supplies. If one does, any caller can run any
/// <c>[TraxAuthorize]</c> train.
/// </para>
/// <para>
/// <strong>How far it reaches.</strong> The state lives in a static <c>AsyncLocal</c>, so it is
/// shared by every instance in the process (not per container), follows <c>await</c>, and does
/// not reach unrelated requests. Await what you start inside a scope, and dispose the handle on the
/// flow that opened it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using (trustedScope.BeginTrusted("billing.nightly-import"))
/// {
///     await trainExecutionService.QueueAsync(trainName, inputJson);
/// }
/// </code>
/// </example>
public interface ITrustedExecutionScope
{
    /// <summary>
    /// True while a trusted scope opened on this async flow (or on the flow that started it) is
    /// active. Authorization code reads it to decide whether to skip its checks.
    /// </summary>
    bool IsTrusted { get; }

    /// <summary>
    /// The reason passed to the innermost active <see cref="BeginTrusted"/> call, or <c>null</c>
    /// when no scope is active. For logs only: never use it to decide whether to trust.
    /// </summary>
    string? CurrentReason { get; }

    /// <summary>
    /// Opens a trusted scope on the current async flow. Dispose the returned handle to close it;
    /// disposing it twice is harmless.
    /// Scopes nest; inner scope wins for <see cref="CurrentReason"/>. Disposing a scope
    /// while an inner one is still open closes it without ending the inner one, and when the
    /// inner one is disposed the flow returns to the nearest scope that is still open, or to
    /// untrusted. A disposed scope never becomes current again.
    /// </summary>
    /// <param name="reason">
    /// Short identifier for the bypass (e.g. <c>"scheduler.local-worker"</c>).
    /// Surfaces in diagnostic logs when a train is executed under trust.
    /// </param>
    /// <returns>The handle that ends the scope when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is null, empty or whitespace.</exception>
    IDisposable BeginTrusted(string reason);
}
