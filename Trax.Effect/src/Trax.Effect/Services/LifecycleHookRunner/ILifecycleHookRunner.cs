using Trax.Effect.Models.Metadata;

namespace Trax.Effect.Services.LifecycleHookRunner;

/// <summary>
/// Coordinates multiple <see cref="TrainLifecycleHook.ITrainLifecycleHook"/> implementations,
/// broadcasting train lifecycle events to all registered hooks.
/// </summary>
public interface ILifecycleHookRunner : IDisposable
{
    Task OnStarted(Metadata metadata, CancellationToken ct);
    Task OnCompleted(Metadata metadata, CancellationToken ct);
    Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct);
    Task OnCancelled(Metadata metadata, CancellationToken ct);
}
