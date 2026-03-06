using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Services.LifecycleHookRunner;

/// <summary>
/// Composite that broadcasts train lifecycle events to all registered hooks.
/// Exceptions in individual hooks are caught and logged — a failing hook never causes
/// the train itself to fail.
/// </summary>
public class LifecycleHookRunner : ILifecycleHookRunner
{
    private readonly List<ITrainLifecycleHook> _hooks;
    private readonly ILogger<LifecycleHookRunner>? _logger;

    public LifecycleHookRunner(
        IEnumerable<ITrainLifecycleHookFactory> hookFactories,
        IEffectRegistry effectRegistry,
        ILogger<LifecycleHookRunner>? logger = null
    )
    {
        _logger = logger;
        _hooks = hookFactories
            .Where(factory => effectRegistry.IsEnabled(factory.GetType()))
            .Select(factory => factory.Create())
            .ToList();
    }

    public async Task OnStarted(Metadata metadata, CancellationToken ct)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                await hook.OnStarted(metadata, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Lifecycle hook ({HookType}) threw on OnStarted for train ({TrainName}).",
                    hook.GetType().Name,
                    metadata.Name
                );
            }
        }

        await OnStateChanged(metadata, ct);
    }

    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                await hook.OnCompleted(metadata, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Lifecycle hook ({HookType}) threw on OnCompleted for train ({TrainName}).",
                    hook.GetType().Name,
                    metadata.Name
                );
            }
        }

        await OnStateChanged(metadata, ct);
    }

    public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                await hook.OnFailed(metadata, exception, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Lifecycle hook ({HookType}) threw on OnFailed for train ({TrainName}).",
                    hook.GetType().Name,
                    metadata.Name
                );
            }
        }

        await OnStateChanged(metadata, ct);
    }

    public async Task OnCancelled(Metadata metadata, CancellationToken ct)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                await hook.OnCancelled(metadata, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Lifecycle hook ({HookType}) threw on OnCancelled for train ({TrainName}).",
                    hook.GetType().Name,
                    metadata.Name
                );
            }
        }

        await OnStateChanged(metadata, ct);
    }

    public async Task OnStateChanged(Metadata metadata, CancellationToken ct)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                await hook.OnStateChanged(metadata, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Lifecycle hook ({HookType}) threw on OnStateChanged for train ({TrainName}).",
                    hook.GetType().Name,
                    metadata.Name
                );
            }
        }
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            if (hook is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(
                        ex,
                        "Failed to dispose lifecycle hook ({HookType}).",
                        hook.GetType().Name
                    );
                }
            }
        }

        _hooks.Clear();
    }
}
