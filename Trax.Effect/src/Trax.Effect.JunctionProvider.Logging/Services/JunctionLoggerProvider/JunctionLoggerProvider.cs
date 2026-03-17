using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;

namespace Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;

public class JunctionLoggerProvider(
    ITraxEffectConfiguration configuration,
    ILogger<JunctionLoggerProvider> logger
) : IJunctionLoggerProvider
{
    public async Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (effectJunction.Metadata is null)
            throw new TrainException(
                "Effect Junction's Metadata should be null. Something has gone horribly wrong."
            );

        logger.Log(configuration.LogLevel, "{@JunctionMetadata}", effectJunction.Metadata);
    }

    public async Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (effectJunction.Metadata is null)
            throw new TrainException(
                "Effect Junction's Metadata should be null. Something has gone horribly wrong."
            );

        effectJunction.Result.Match(
            Right: resultOut =>
            {
                if (resultOut is null)
                    return;

                effectJunction.Metadata.OutputJson = configuration.SerializeJunctionData
                    ? JsonSerializer.Serialize<object>(
                        resultOut,
                        TraxJsonSerializationOptions.JunctionLogging
                    )
                    : null;
            },
            Left: _ => { },
            Bottom: () => { }
        );

        logger.Log(configuration.LogLevel, "{@Metadata}", effectJunction.Metadata);
    }

    public void Dispose() { }
}
