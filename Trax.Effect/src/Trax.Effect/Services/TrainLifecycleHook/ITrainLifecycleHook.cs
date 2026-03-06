using Trax.Effect.Models.Metadata;

namespace Trax.Effect.Services.TrainLifecycleHook;

/// <summary>
/// Hook interface for reacting to train state transitions.
/// Implement this to create custom side effects (e.g., metrics, alerts, subscriptions).
/// Default interface methods allow implementations to override only the events they care about.
/// </summary>
public interface ITrainLifecycleHook
{
    Task OnStarted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
    Task OnCompleted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
    Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        Task.CompletedTask;
    Task OnCancelled(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called on every state transition (Started, Completed, Failed, Cancelled).
    /// Useful for unified event streams where callers don't want separate subscriptions per state.
    /// </summary>
    Task OnStateChanged(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
}
