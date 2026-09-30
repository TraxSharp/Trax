using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
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
/// <param name="outputPolicy">Decides whether a junction output is serialized and under what ceiling; the
/// default policy when none is registered.</param>
internal class JunctionLoggerProvider(
    ITraxEffectConfiguration configuration,
    ILogger<JunctionLoggerProvider> logger,
    ILifecycleHookOutputPolicy? outputPolicy = null
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
                "Effect Junction's Metadata should not be null. Something has gone horribly wrong."
            );

        logger.Log(configuration.LogLevel, "{@JunctionMetadata}", effectJunction.Metadata);
    }

    /// <summary>
    /// Logs the junction's metadata after it ran. When the junction succeeded with a non-null result and
    /// junction data serialization is on, first stores that result as JSON in the metadata's
    /// <c>OutputJson</c>, with sensitive members masked, bounded and left out as the train's own output
    /// is for lifecycle hooks, and replaced by a placeholder when it cannot be serialized; otherwise
    /// <c>OutputJson</c> is left as it was. Never fails the train over the output.
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
                "Effect Junction's Metadata should not be null. Something has gone horribly wrong."
            );

        effectJunction.Result.Match(
            Right: resultOut =>
            {
                if (resultOut is null)
                    return;

                effectJunction.Metadata.OutputJson = configuration.SerializeJunctionData
                    ? SerializeForLog(resultOut, serviceTrain.TrainName)
                    : null;
            },
            Left: _ => { },
            Bottom: () => { }
        );

        logger.Log(configuration.LogLevel, "{@Metadata}", effectJunction.Metadata);
    }

    /// <summary>
    /// Serializes a junction's output for the log under the same decision and ceiling as the copy
    /// lifecycle hooks get: nothing for a train whose output the host excluded, and a
    /// <c>{"_truncated": true, ...}</c> placeholder past the ceiling. The junction already
    /// succeeded, so an output the serializer cannot represent (a <see cref="Type"/> member, a graph
    /// deeper than the options allow, a throwing getter) is logged as
    /// <c>{"_unserializable": true, "_error": "&lt;exception type&gt;"}</c> rather than failing the train.
    /// </summary>
    private string? SerializeForLog(object output, string trainName)
    {
        var ceiling = (outputPolicy ?? DefaultPolicy).MaxCopyBytes(trainName);
        if (ceiling is null)
            return null;

        try
        {
            return TraxBoundedJson.Serialize(
                output,
                TraxLogSerialization.ForLogging(TraxJsonSerializationOptions.JunctionLogging),
                ceiling
            );
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Could not serialize the output of a junction of train ({TrainName}) for the log.",
                trainName
            );
            return JsonSerializer.Serialize(
                new { _unserializable = true, _error = ex.GetType().Name }
            );
        }
    }

    private static readonly ILifecycleHookOutputPolicy DefaultPolicy =
        new DefaultLifecycleHookOutputPolicy();

    /// <summary>Holds no resources; does nothing.</summary>
    public void Dispose() { }
}
