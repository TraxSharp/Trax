using Microsoft.Extensions.Logging;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.StepEffectProvider;
using Trax.Effect.Services.StepEffectProviderFactory;

namespace Trax.Effect.Services.StepEffectRunner;

public class StepEffectRunner : IStepEffectRunner
{
    private List<IStepEffectProvider> ActiveStepEffectProviders { get; init; }

    private readonly ILogger<StepEffectRunner>? _logger;

    public StepEffectRunner(
        IEnumerable<IStepEffectProviderFactory> stepEffectProviderFactories,
        IEffectRegistry effectRegistry,
        ILogger<StepEffectRunner>? logger = null
    )
    {
        _logger = logger;

        ActiveStepEffectProviders = [];
        ActiveStepEffectProviders.AddRange(
            stepEffectProviderFactories
                .Where(factory => effectRegistry.IsEnabled(factory.GetType()))
                .RunAll(factory => factory.Create())
        );
    }

    public async Task BeforeStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        await ActiveStepEffectProviders.RunAllAsync(provider =>
            provider.BeforeStepExecution(effectStep, serviceTrain, cancellationToken)
        );
    }

    public async Task AfterStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        await ActiveStepEffectProviders.RunAllAsync(provider =>
            provider.AfterStepExecution(effectStep, serviceTrain, cancellationToken)
        );
    }

    public void Dispose()
    {
        var disposalExceptions = new List<Exception>();

        foreach (var provider in ActiveStepEffectProviders)
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

        ActiveStepEffectProviders.Clear();

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
                ActiveStepEffectProviders.Count
            );
        }
    }
}
