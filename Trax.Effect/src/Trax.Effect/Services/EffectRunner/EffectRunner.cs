using Trax.Effect.Extensions;
using Trax.Effect.Models;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Services.EffectRunner;

/// <summary>
/// Coordinates multiple effect providers to track and persist workflow metadata.
/// This class acts as a facade over all registered effect providers, delegating
/// operations to each provider and managing their lifecycle.
/// </summary>
/// <remarks>
/// The EffectRunner is the central component for managing side effects in the Trax.Effect system.
/// It's responsible for:
/// 1. Creating and managing effect providers through their factories
/// 2. Tracking models across all providers
/// 3. Persisting changes to all providers
/// 4. Properly disposing of providers when they're no longer needed
///
/// This design follows the Composite pattern, allowing multiple effect providers
/// to be treated as a single unit.
/// </remarks>
public class EffectRunner : IEffectRunner
{
    /// <summary>
    /// Collection of active effect providers that will process tracking and persistence operations.
    /// </summary>
    /// <remarks>
    /// Each provider in this collection represents a different storage or processing mechanism
    /// for workflow metadata (e.g., database, logging, etc.).
    /// </remarks>
    private List<IEffectProvider> ActiveEffectProviders { get; init; }

    /// <summary>
    /// Logger for tracking disposal operations and errors.
    /// </summary>
    private readonly ILogger<EffectRunner>? _logger;

    /// <summary>
    /// Initializes a new instance of the EffectRunner with the specified effect provider factories.
    /// </summary>
    /// <param name="effectProviderFactories">Collection of factories that create effect providers</param>
    /// <param name="effectRegistry">Registry used to determine which effect providers are enabled</param>
    /// <param name="logger">Optional logger for tracking operations and errors</param>
    /// <remarks>
    /// During initialization, the runner:
    /// 1. Creates an empty list of active providers
    /// 2. Calls Create() on each factory to instantiate the providers
    /// 3. Adds all created providers to the active providers list
    ///
    /// This approach follows the Factory pattern, allowing for flexible provider creation
    /// and configuration through dependency injection.
    /// </remarks>
    public EffectRunner(
        IEnumerable<IEffectProviderFactory> effectProviderFactories,
        IEffectRegistry effectRegistry,
        ILogger<EffectRunner>? logger = null
    )
    {
        _logger = logger;
        ActiveEffectProviders = [];

        ActiveEffectProviders.AddRange(
            effectProviderFactories
                .Where(factory => effectRegistry.IsEnabled(factory.GetType()))
                .RunAll(factory => factory.Create())
        );
    }

    /// <summary>
    /// Persists any pending changes across all active effect providers.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method calls SaveChanges on each active provider in parallel,
    /// allowing for efficient persistence across multiple storage mechanisms.
    /// The RunAllAsync extension method ensures all providers are called
    /// regardless of exceptions in individual providers.
    /// </remarks>
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        await ActiveEffectProviders.RunAllAsync(
            provider => provider.SaveChanges(cancellationToken)
        );
    }

    /// <summary>
    /// Tracks a model across all active effect providers.
    /// </summary>
    /// <param name="model">The model to track</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method calls Track on each active provider,
    /// allowing the model to be processed by all registered providers.
    /// The RunAll extension method ensures all providers are called
    /// regardless of exceptions in individual providers.
    /// </remarks>
    public async Task Track(IModel model)
    {
        ActiveEffectProviders.RunAll(provider => provider.Track(model));
    }

    /// <inheritdoc />
    public async Task Update(IModel model)
    {
        ActiveEffectProviders.RunAll(provider => provider.Update(model));
    }

    /// <summary>
    /// Disposes of all active effect providers and clears the collection.
    /// </summary>
    /// <remarks>
    /// This method ensures proper cleanup of resources used by effect providers.
    /// It's called automatically when the EffectRunner is disposed.
    /// </remarks>
    public void Dispose() => DeactivateProviders();

    /// <summary>
    /// Helper method to dispose of all active providers and clear the collection.
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Attempts to dispose each active provider individually
    /// 2. Logs any disposal failures but continues with remaining providers
    /// 3. Clears the collection of active providers
    ///
    /// This implementation ensures that all providers get a chance to dispose
    /// even if some providers throw exceptions during disposal.
    /// </remarks>
    private void DeactivateProviders()
    {
        var disposalExceptions = new List<Exception>();

        foreach (var provider in ActiveEffectProviders)
        {
            try
            {
                provider?.Dispose();
            }
            catch (Exception ex)
            {
                disposalExceptions.Add(ex);
                _logger?.LogError(
                    ex,
                    "Failed to dispose effect provider of type ({ProviderType}). Provider disposal will continue for remaining providers.",
                    provider?.GetType().Name ?? "Unknown"
                );
            }
        }

        ActiveEffectProviders.Clear();

        // If we had disposal exceptions, log the summary
        if (disposalExceptions.Count > 0)
        {
            _logger?.LogWarning(
                "Completed provider disposal with ({ExceptionCount}) provider(s) failing to dispose properly. "
                    + "Memory leaks may have occurred in the failed providers.",
                disposalExceptions.Count
            );
        }
        else
        {
            _logger?.LogTrace(
                "Successfully disposed all ({ProviderCount}) effect provider(s).",
                ActiveEffectProviders.Count
            );
        }
    }
}
