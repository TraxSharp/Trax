using System.Text.Json;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Host;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Extensions;

internal static class ServiceTrainExtensions
{
    /// <summary>
    /// Initializes the train metadata in the database and sets the initial state.
    /// </summary>
    internal static async Task<Unit> InitializeServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();

        serviceTrain.Logger?.LogTrace("Initializing ({TrainName})", serviceTrain.TrainName);
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = serviceTrain.TrainName,
                ExternalId = serviceTrain.ExternalId,
                Input = null,
                ParentId = serviceTrain.ParentId,
            }
        );

        return await serviceTrain.InitializeServiceTrain(metadata);
    }

    /// <summary>
    /// Initializes the train metadata in the database and sets the initial state.
    /// </summary>
    internal static async Task<Unit> InitializeServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Metadata metadata
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();

        if (metadata.TrainState != TrainState.Pending)
            throw new TrainException(
                $"Cannot start a train with state ({metadata.TrainState}), must be Pending."
            );

        await serviceTrain.EffectRunner.Track(metadata);
        serviceTrain.Logger?.LogTrace("Initializing ({TrainName})", serviceTrain.TrainName);
        serviceTrain.Metadata = metadata;

        return await serviceTrain.StartServiceTrain(metadata);
    }

    internal static async Task<Unit> StartServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Metadata metadata
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();
        serviceTrain.Metadata.AssertLoaded();

        if (metadata.TrainState != TrainState.Pending)
            throw new TrainException(
                $"Cannot start a train with state ({metadata.TrainState}), must be Pending."
            );

        serviceTrain.Logger?.LogTrace(
            "Setting ({TrainName}) to In Progress.",
            serviceTrain.TrainName
        );
        serviceTrain.Metadata.TrainState = TrainState.InProgress;

        // Stamp execution host identity — overwrites any values set by the dispatcher
        // so the metadata reflects WHERE the train actually ran.
        if (TraxHostInfo.Current is { } host)
        {
            serviceTrain.Metadata.HostName = host.HostName;
            serviceTrain.Metadata.HostEnvironment = host.HostEnvironment;
            serviceTrain.Metadata.HostInstanceId = host.HostInstanceId;
            serviceTrain.Metadata.HostLabels = host.Labels is { Count: > 0 }
                ? JsonSerializer.Serialize(host.Labels)
                : null;
        }

        await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);

        return Unit.Default;
    }

    /// <summary>
    /// Updates the train metadata to reflect the final state of the execution.
    /// </summary>
    internal static async Task<Unit> FinishServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Either<Exception, TOut> result
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();
        serviceTrain.Metadata.AssertLoaded();

        var failureReason = result.IsRight ? null : result.Swap().ValueUnsafe();

        var resultState =
            result.IsRight ? TrainState.Completed
            : failureReason is OperationCanceledException ? TrainState.Cancelled
            : TrainState.Failed;
        serviceTrain.Logger?.LogTrace(
            "Setting ({TrainName}) to ({ResultState}).",
            serviceTrain.TrainName,
            resultState.ToString()
        );
        serviceTrain.Metadata.TrainState = resultState;
        serviceTrain.Metadata.EndTime = DateTime.UtcNow;
        serviceTrain.Metadata.CurrentlyRunningJunction = null;
        serviceTrain.Metadata.JunctionStartedAt = null;

        if (failureReason != null)
        {
            // Classify before recording, so the class lands on the metadata with the rest of the
            // failure and travels with the exception data if this run is reported somewhere else.
            // Only real failures are classified — a cancellation is not one.
            var classified =
                resultState == TrainState.Failed ? Classify(serviceTrain, failureReason) : null;

            serviceTrain.Metadata.AddException(failureReason);

            // A class the failure already carried, from a remote worker or a junction, was
            // decided where the real exception was held and wins. The classifier's answer
            // applies to everything else, including a failure raised outside any junction,
            // which carries no exception data for it to be written onto.
            if (
                classified is { } failureClass
                && serviceTrain.Metadata.FailureClass == FailureClass.Unclassified
            )
                serviceTrain.Metadata.FailureClass = failureClass;
        }

        await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);

        return Unit.Default;
    }

    /// <summary>
    /// Asks the registered <see cref="IFailureClassifier"/> what kind of failure this was, and
    /// writes the answer onto the exception's structured data when it has some, so the class
    /// travels with the failure if it is reported somewhere else.
    /// </summary>
    /// <remarks>
    /// A classifier is optional, and one that throws must not mask the failure it was asked about:
    /// its exception is logged and the failure stays unclassified.
    /// </remarks>
    private static FailureClass? Classify<TIn, TOut>(
        ServiceTrain<TIn, TOut> serviceTrain,
        Exception failureReason
    )
    {
        // A failure rebuilt from a serialized record, which is how a remote run's failure
        // arrives, was decided where the real exception was held. If that side sent no class the
        // failure stays unclassified: classifying the rebuilt exception here would be judging a
        // type name the calling side never saw.
        if (IsRebuiltFailure(failureReason))
            return null;

        try
        {
            var classifier = serviceTrain.ServiceProvider?.GetService<IFailureClassifier>();

            if (classifier?.Classify(failureReason) is not { } failureClass)
                return null;

            // A classifier is consumer code, so it can return any value the enum's underlying type
            // holds. An undefined one reaches the provider as an enum it cannot map: the write
            // throws inside SaveOutcome, the row is left InProgress holding its subject, and the
            // stale reaper records Failed an hour later. Normalised here the same way
            // RemoteRunJson.TolerantFailureClassConverter normalises a class off the remote wire.
            if (!Enum.IsDefined(failureClass))
                failureClass = FailureClass.Unclassified;

            if (failureReason.Data["TrainExceptionData"] is TrainExceptionData data)
            {
                data.FailureClass ??= failureClass;
            }
            else
            {
                // A failure raised outside any junction carries no data yet. Attach it, with
                // the fields AddException would derive anyway, so the class travels with the
                // failure when a remote worker reports it back.
                failureReason.Data["TrainExceptionData"] = new TrainExceptionData
                {
                    TrainName = serviceTrain.TrainName,
                    TrainExternalId = serviceTrain.ExternalId,
                    Type = failureReason.GetType().Name,
                    Junction = "TrainException",
                    Message = failureReason.Message,
                    StackTrace = failureReason.StackTrace,
                    FailureClass = failureClass,
                };
            }

            return failureClass;
        }
        catch (Exception ex)
        {
            serviceTrain.Logger?.LogWarning(
                ex,
                "Failure classifier threw for train ({TrainName}); recording the failure as unclassified.",
                serviceTrain.TrainName
            );

            return null;
        }
    }

    private static bool IsRebuiltFailure(Exception failure)
    {
        if (failure is not TrainException || !failure.Message.StartsWith('{'))
            return false;

        try
        {
            return JsonSerializer.Deserialize<TrainExceptionData>(failure.Message) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
