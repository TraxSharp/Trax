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

    /// <summary>
    /// Builds the enabled hooks for one run from <paramref name="serviceProvider"/>, the provider
    /// the runner was resolved from. The runner is transient and a train resolves it from its
    /// run's scope, so a hook's scoped dependencies are that run's.
    /// </summary>
    public LifecycleHookRunner(
        IEnumerable<ITrainLifecycleHookFactory> hookFactories,
        IEffectRegistry effectRegistry,
        IServiceProvider serviceProvider,
        ILogger<LifecycleHookRunner>? logger = null
    )
        : this(hookFactories, effectRegistry, factory => factory.Create(serviceProvider), logger)
    { }

    /// <summary>
    /// Builds the enabled hooks through each factory's parameterless <c>Create()</c>, with no
    /// scope to take services from.
    /// </summary>
    public LifecycleHookRunner(
        IEnumerable<ITrainLifecycleHookFactory> hookFactories,
        IEffectRegistry effectRegistry,
        ILogger<LifecycleHookRunner>? logger = null
    )
        : this(hookFactories, effectRegistry, factory => factory.Create(), logger) { }

    private LifecycleHookRunner(
        IEnumerable<ITrainLifecycleHookFactory> hookFactories,
        IEffectRegistry effectRegistry,
        Func<ITrainLifecycleHookFactory, ITrainLifecycleHook> create,
        ILogger<LifecycleHookRunner>? logger
    )
    {
        _logger = logger;
        _hooks = hookFactories
            .Where(factory => effectRegistry.IsEnabled(factory.GetType()))
            .Select(create)
            .ToList();
    }

    /// <summary>
    /// Calls <c>OnStarted</c> on each enabled hook in registration order, then <see cref="OnStateChanged"/>. Each hook's exception, including an
    /// <see cref="OperationCanceledException"/>, is logged and swallowed, so one failing hook neither stops the
    /// others nor fails the train.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">Passed through to each hook.</param>
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

    /// <summary>
    /// Calls <c>OnCompleted</c> on each enabled hook in registration order, then <see cref="OnStateChanged"/>. Each hook's exception, including an
    /// <see cref="OperationCanceledException"/>, is logged and swallowed, so one failing hook neither stops the
    /// others nor fails the train.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">Passed through to each hook.</param>
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

    /// <summary>
    /// Calls <c>OnFailed</c> on each enabled hook in registration order, then <see cref="OnStateChanged"/>. Each hook's exception, including an
    /// <see cref="OperationCanceledException"/>, is logged and swallowed, so one failing hook neither stops the
    /// others nor fails the train.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="exception">The exception that failed the train.</param>
    /// <param name="ct">Passed through to each hook.</param>
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

    /// <summary>
    /// Calls <c>OnCancelled</c> on each enabled hook in registration order, then <see cref="OnStateChanged"/>. Each hook's exception, including an
    /// <see cref="OperationCanceledException"/>, is logged and swallowed, so one failing hook neither stops the
    /// others nor fails the train.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">Passed through to each hook.</param>
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

    /// <summary>
    /// Calls <c>OnStateChanged</c> on each enabled hook in registration order. Each hook's exception, including an
    /// <see cref="OperationCanceledException"/>, is logged and swallowed, so one failing hook neither stops the
    /// others nor fails the train.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">Passed through to each hook.</param>
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

    /// <summary>
    /// Disposes each hook that implements <see cref="IDisposable"/>, logging and continuing past any that throw.
    /// </summary>
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
