using Trax.Effect.Models.Metadata;

namespace Trax.Effect.Services.TrainLifecycleHook;

/// <summary>
/// Hook interface for reacting to train state transitions.
/// Implement this to create custom side effects (e.g., metrics, alerts, subscriptions).
/// Default interface methods allow implementations to override only the events they care about.
/// </summary>
public interface ITrainLifecycleHook
{
    /// <summary>
    /// Called when a run starts: its row is <c>InProgress</c> and saved, and its input object is set, but no
    /// junction has run yet. Exceptions are logged and swallowed; they never fail the train.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">The caller's cancellation token.</param>
    Task OnStarted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called after a run's <c>Completed</c> outcome is saved. <c>metadata.Output</c> holds the serialized output
    /// when the parameter effect stored it or the output policy allowed a bounded copy, otherwise <c>null</c>.
    /// Exceptions are logged and swallowed.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct"><see cref="CancellationToken.None"/>: reporting the outcome is not cancelled with the
    /// work.</param>
    Task OnCompleted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called after a run's <c>Failed</c> outcome is saved, for any failure other than a requested cancellation
    /// (a timeout that is not the caller's cancellation lands here). Exceptions are logged and swallowed.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="exception">The exception that failed the train.</param>
    /// <param name="ct"><see cref="CancellationToken.None"/>.</param>
    Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>
    /// Called after a run's <c>Cancelled</c> outcome is saved, when the train stopped with an <see cref="OperationCanceledException"/> while its
    /// cancellation token was cancelled or cancellation was requested on its row. Exceptions are logged and swallowed.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct"><see cref="CancellationToken.None"/>.</param>
    Task OnCancelled(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called on every state transition (Started, Completed, Failed, Cancelled).
    /// Useful for unified event streams where callers don't want separate subscriptions per state.
    /// </summary>
    Task OnStateChanged(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
}
