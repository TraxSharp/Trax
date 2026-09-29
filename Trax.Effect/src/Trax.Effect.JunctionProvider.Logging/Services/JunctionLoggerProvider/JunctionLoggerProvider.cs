using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;

namespace Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;

/// <summary>
/// The junction logger: logs each junction's metadata through <c>ILogger&lt;JunctionLoggerProvider&gt;</c>
/// at the effect's configured log level, before and after the junction runs. Registered by
/// <c>AddJunctionLogger</c>; not intended to be constructed directly.
/// </summary>
/// <param name="configuration">Supplies the log level and whether junction output is serialized.</param>
/// <param name="logger">The logger entries are written to.</param>
public class JunctionLoggerProvider(
    ITraxEffectConfiguration configuration,
    ILogger<JunctionLoggerProvider> logger
) : IJunctionLoggerProvider
{
    /// <summary>
    /// Logs the junction's metadata as it is about to run.
    /// </summary>
    /// <typeparam name="TIn">The junction's input type.</typeparam>
    /// <typeparam name="TOut">The junction's output type.</typeparam>
    /// <typeparam name="TTrainIn">The train's input type.</typeparam>
    /// <typeparam name="TTrainOut">The train's output type.</typeparam>
    /// <param name="effectJunction">The junction about to run.</param>
    /// <param name="serviceTrain">The train running it.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <exception cref="TrainException">The junction has no metadata, which the effect pipeline never allows.</exception>
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

    /// <summary>
    /// Logs the junction's metadata after it ran. When the junction succeeded with a non-null result and
    /// junction data serialization is on, first stores that result as JSON in the metadata's
    /// <c>OutputJson</c>, with sensitive members masked; otherwise <c>OutputJson</c> is left as it was.
    /// </summary>
    /// <typeparam name="TIn">The junction's input type.</typeparam>
    /// <typeparam name="TOut">The junction's output type.</typeparam>
    /// <typeparam name="TTrainIn">The train's input type.</typeparam>
    /// <typeparam name="TTrainOut">The train's output type.</typeparam>
    /// <param name="effectJunction">The junction that ran.</param>
    /// <param name="serviceTrain">The train running it.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <exception cref="TrainException">The junction has no metadata, which the effect pipeline never allows.</exception>
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
                        TraxLogSerialization.ForLogging(
                            TraxJsonSerializationOptions.JunctionLogging
                        )
                    )
                    : null;
            },
            Left: _ => { },
            Bottom: () => { }
        );

        logger.Log(configuration.LogLevel, "{@Metadata}", effectJunction.Metadata);
    }

    /// <summary>Holds no resources; does nothing.</summary>
    public void Dispose() { }
}
