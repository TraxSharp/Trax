using Microsoft.Extensions.Logging;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.JunctionEffectRunner;

/// <summary>
/// Default <see cref="IJunctionEffectRunner"/>, registered as transient so each train run gets its own providers.
/// Infrastructure; not intended to be constructed or called directly.
/// </summary>
internal class JunctionEffectRunner : IJunctionEffectRunner
{
    private List<IJunctionEffectProvider> ActiveJunctionEffectProviders { get; init; }

    private readonly ILogger<JunctionEffectRunner>? _logger;

    /// <summary>
    /// Creates one provider from each factory the effect registry reports as enabled at this moment; toggling an
    /// effect later affects only runners created after the change. An exception from a factory's
    /// <c>Create()</c> propagates.
    /// </summary>
    /// <param name="junctionEffectProviderFactories">Every registered junction effect factory.</param>
    /// <param name="effectRegistry">Decides which factories are enabled.</param>
    /// <param name="logger">Receives disposal failures; optional.</param>
    public JunctionEffectRunner(
        IEnumerable<IJunctionEffectProviderFactory> junctionEffectProviderFactories,
        IEffectRegistry effectRegistry,
        ILogger<JunctionEffectRunner>? logger = null
    )
    {
        _logger = logger;

        ActiveJunctionEffectProviders = [];
        ActiveJunctionEffectProviders.AddRange(
            junctionEffectProviderFactories
                .Where(factory => effectRegistry.IsEnabled(factory.GetType()))
                .RunAll(factory => factory.Create())
        );
    }

    /// <summary>
    /// Awaits each active provider's <c>BeforeJunctionExecution</c> in turn. The first exception stops the
    /// remaining providers and propagates.
    /// </summary>
    /// <param name="effectJunction">The junction being run; its <c>Metadata</c> is already created.</param>
    /// <param name="serviceTrain">The train running the junction.</param>
    /// <param name="cancellationToken">The train's cancellation token.</param>
    public async Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        foreach (var provider in ActiveJunctionEffectProviders)
            await provider.BeforeJunctionExecution(effectJunction, serviceTrain, cancellationToken);
    }

    /// <summary>
    /// Awaits each active provider's <c>AfterJunctionExecution</c> in turn. The first exception stops the
    /// remaining providers and propagates.
    /// </summary>
    /// <param name="effectJunction">The junction being run; its <c>Metadata</c> is already created.</param>
    /// <param name="serviceTrain">The train running the junction.</param>
    /// <param name="cancellationToken">The train's cancellation token.</param>
    public async Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        foreach (var provider in ActiveJunctionEffectProviders)
            await provider.AfterJunctionExecution(effectJunction, serviceTrain, cancellationToken);
    }

    /// <summary>
    /// Disposes every provider, logging and swallowing each provider's exception so the rest are still disposed,
    /// then clears the list.
    /// </summary>
    public void Dispose()
    {
        var disposalExceptions = new List<Exception>();
        var providerCount = ActiveJunctionEffectProviders.Count;

        foreach (var provider in ActiveJunctionEffectProviders)
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

        ActiveJunctionEffectProviders.Clear();

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
                providerCount
            );
        }
    }
}
