using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;
using JsonConverter = System.Text.Json.Serialization.JsonConverter;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Trax.Effect.StepProvider.Logging.Services.StepLoggerProvider;

public class StepLoggerProvider(
    ITraxEffectConfiguration configuration,
    ILogger<StepLoggerProvider> logger
) : IStepLoggerProvider
{
    public async Task BeforeStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (effectStep.Metadata is null)
            throw new TrainException(
                "Effect Step's Metadata should be null. Something has gone horribly wrong."
            );

        logger.Log(configuration.LogLevel, "{@StepMetadata}", effectStep.Metadata);
    }

    public async Task AfterStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (effectStep.Metadata is null)
            throw new TrainException(
                "Effect Step's Metadata should be null. Something has gone horribly wrong."
            );

        effectStep.Result.Match(
            Right: resultOut =>
            {
                if (resultOut is null)
                    return;

                effectStep.Metadata.OutputJson = configuration.SerializeStepData
                    ? JsonConvert.SerializeObject(
                        resultOut,
                        configuration.NewtonsoftJsonSerializerSettings
                    )
                    : null;
            },
            Left: _ => { },
            Bottom: () => { }
        );

        logger.Log(configuration.LogLevel, "{@Metadata}", effectStep.Metadata);
    }

    public void Dispose() { }
}
