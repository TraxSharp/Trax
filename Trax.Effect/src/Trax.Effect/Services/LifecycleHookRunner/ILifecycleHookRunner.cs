using Trax.Effect.Models.Metadata;

namespace Trax.Effect.Services.LifecycleHookRunner;

/// <summary>
/// Coordinates multiple <see cref="TrainLifecycleHook.ITrainLifecycleHook"/> implementations,
/// broadcasting train lifecycle events to all registered hooks.
/// </summary>
public interface ILifecycleHookRunner : IDisposable
{
    /// <summary>
    /// Calls <c>OnStarted</c> on every hook, then <see cref="OnStateChanged"/>. Called once the run's row is
    /// <c>InProgress</c> and persisted, before the first junction.
    /// </summary>
    /// <param name="metadata">The run's metadata row, with its input object set.</param>
    /// <param name="ct">The caller's cancellation token.</param>
    Task OnStarted(Metadata metadata, CancellationToken ct);

    /// <summary>
    /// Calls <c>OnCompleted</c> on every hook, then <see cref="OnStateChanged"/>. Called after the completed
    /// outcome is saved.
    /// </summary>
    /// <param name="metadata">The run's metadata row, <c>Completed</c>, with its output set.</param>
    /// <param name="ct">A token the train passes as <see cref="CancellationToken.None"/>, so reporting the
    /// outcome is not cancelled with the work.</param>
    Task OnCompleted(Metadata metadata, CancellationToken ct);

    /// <summary>
    /// Calls <c>OnFailed</c> on every hook, then <see cref="OnStateChanged"/>. Called after the failure is saved,
    /// for any failure other than a requested cancellation.
    /// </summary>
    /// <param name="metadata">The run's metadata row, <c>Failed</c>.</param>
    /// <param name="exception">The exception that failed the train.</param>
    /// <param name="ct">A token the train passes as <see cref="CancellationToken.None"/>.</param>
    Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct);

    /// <summary>
    /// Calls <c>OnCancelled</c> on every hook, then <see cref="OnStateChanged"/>. Called after the cancelled
    /// outcome is saved, when the train stopped with an
    /// <see cref="OperationCanceledException"/> while its token was cancelled or cancellation was requested on its row.
    /// </summary>
    /// <param name="metadata">The run's metadata row, <c>Cancelled</c>.</param>
    /// <param name="ct">A token the train passes as <see cref="CancellationToken.None"/>.</param>
    Task OnCancelled(Metadata metadata, CancellationToken ct);

    /// <summary>
    /// Calls <c>OnStateChanged</c> on every hook. The other four methods call this themselves after their own
    /// event, so a caller does not call it separately for those transitions.
    /// </summary>
    /// <param name="metadata">The run's metadata row in its new state.</param>
    /// <param name="ct">Passed through to each hook.</param>
    Task OnStateChanged(Metadata metadata, CancellationToken ct);
}
