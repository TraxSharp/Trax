using Microsoft.Extensions.Logging;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.JunctionEffectRunner;

public class JunctionEffectRunner : IJunctionEffectRunner
{
    private List<IJunctionEffectProvider> ActiveJunctionEffectProviders { get; init; }

    private readonly ILogger<JunctionEffectRunner>? _logger;

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

    public async Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        await ActiveJunctionEffectProviders.RunAllAsync(provider =>
            provider.BeforeJunctionExecution(effectJunction, serviceTrain, cancellationToken)
        );
    }

    public async Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        await ActiveJunctionEffectProviders.RunAllAsync(provider =>
            provider.AfterJunctionExecution(effectJunction, serviceTrain, cancellationToken)
        );
    }

    public void Dispose()
    {
        var disposalExceptions = new List<Exception>();

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
                ActiveJunctionEffectProviders.Count
            );
        }
    }
}
