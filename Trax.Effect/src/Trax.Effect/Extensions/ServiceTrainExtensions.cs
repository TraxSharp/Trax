using System.Text.Json;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Exceptions;
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

        return await serviceTrain.InitializeServiceTrain(metadata, preCreated: false);
    }

    /// <summary>
    /// Initializes the train metadata in the database and sets the initial state.
    /// </summary>
    /// <param name="serviceTrain">The train about to run.</param>
    /// <param name="metadata">The run's row.</param>
    /// <param name="preCreated">
    /// Whether the row already exists in the store, created by whoever dispatched the run, in which
    /// case the start is claimed there. False for a row this run has just created.
    /// </param>
    internal static async Task<Unit> InitializeServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Metadata metadata,
        bool preCreated = true
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

        return await serviceTrain.StartServiceTrain(metadata, preCreated);
    }

    internal static async Task<Unit> StartServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Metadata metadata,
        bool preCreated
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();
        serviceTrain.Metadata.AssertLoaded();

        if (metadata.TrainState != TrainState.Pending)
            throw new TrainException(
                $"Cannot start a train with state ({metadata.TrainState}), must be Pending."
            );

        // A row that already exists was created by whoever dispatched this run, and the same row
        // can be handed to two executions (an at-least-once queue, a retried dispatch, a job
        // claimed twice). Each holds a copy that says Pending, so the check above passes for both;
        // the store decides which one starts. A row this run created is its own; it is not
        // claimed, and its Id cannot tell the two apart, since the in-memory provider assigns one
        // on tracking.
        if (
            preCreated
            && !await serviceTrain.EffectRunner.TryClaimPendingRun(metadata, CancellationToken.None)
        )
            throw new TrainAlreadyStartedException(metadata.Id, serviceTrain.TrainName);

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
            : serviceTrain.IsRequestedCancellation(failureReason!) ? TrainState.Cancelled
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
            NameTheTrainCanonically(serviceTrain, failureReason);

            // Classify before recording, so the class lands on the metadata with the rest of the
            // failure and travels with the exception data if this run is reported somewhere else.
            // Only real failures are classified. A cancellation something asked for is not one; a
            // cancellation nothing asked for, such as an HttpClient timeout, is.
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
    /// Whether a failure is a cancellation this run was asked for, which records
    /// <see cref="TrainState.Cancelled"/> rather than <see cref="TrainState.Failed"/>.
    /// </summary>
    /// <remarks>
    /// A run is asked to stop in two ways: its own token is cancelled (the caller, a host
    /// shutting down, the scheduler cancelling a run on this host), or the persisted cancel flag
    /// is set (the dashboard, or the scheduler's job timeout for a run on another host), which
    /// <c>CancellationCheckProvider</c> turns into a cancellation at the next junction boundary
    /// and mirrors onto <see cref="Metadata.CancellationRequested"/>. Any other
    /// <see cref="OperationCanceledException"/>, an <c>HttpClient</c> timeout being the common
    /// one, arrives as the same exception type but nobody asked for it, so it is a failure: a
    /// manifest retries only a failed run. See Trax.Docs/adr/0020.
    /// </remarks>
    internal static bool IsRequestedCancellation<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Exception failure
    ) =>
        failure is OperationCanceledException
        && (
            serviceTrain.CancellationToken.IsCancellationRequested
            || serviceTrain.Metadata?.CancellationRequested == true
        );

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

        FailureClass? answer;

        try
        {
            var classifier = serviceTrain.ServiceProvider?.GetService<IFailureClassifier>();
            answer = classifier?.Classify(failureReason);
        }
        catch (Exception ex)
        {
            serviceTrain.Logger?.LogWarning(
                ex,
                "Failure classifier threw for train ({TrainName}); recording the failure as unclassified.",
                serviceTrain.TrainName
            );

            answer = null;
        }

        // A classifier is consumer code, so it can return any value the enum's underlying type
        // holds. An undefined one reaches the provider as an enum it cannot map: the write
        // throws inside SaveOutcome, the row is left InProgress holding its subject, and the
        // stale reaper records Failed an hour later. Normalised here the same way
        // RemoteRunJson.TolerantFailureClassConverter normalises a class off the remote wire.
        if (answer is { } value && !Enum.IsDefined(value))
            answer = FailureClass.Unclassified;

        // A cancellation that reaches here was not asked for (FinishServiceTrain records a
        // requested one as Cancelled and never classifies it), so something gave up on its own:
        // an HttpClient timeout, or a downstream token. That is transient unless the consumer's
        // classifier says otherwise.
        if (
            answer is null or FailureClass.Unclassified
            && failureReason is OperationCanceledException
        )
            answer = FailureClass.Transient;

        if (answer is not { } failureClass)
            return null;

        try
        {
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
        }
        catch (Exception ex)
        {
            // Exception.Data refuses writes on a few framework exception types. The class is
            // still recorded on the run; it just cannot ride on the exception.
            serviceTrain.Logger?.LogWarning(
                ex,
                "Could not attach the failure class to the exception for train ({TrainName}).",
                serviceTrain.TrainName
            );
        }

        return failureClass;
    }

    /// <summary>
    /// Makes the failure data a junction attached name this train the way its metadata row, the
    /// registry and the dashboard do, by its canonical name.
    /// </summary>
    /// <remarks>
    /// Trax.Core's junction records the train's class name, having no other; a failure raised
    /// outside any junction is recorded here with the canonical name. Without this, one train
    /// appeared under two names depending on where it failed. Only data recorded for this run is
    /// renamed: a junction overwrites the data of a nested train's failure that passes through it,
    /// so data carrying another run's external id did not come from this one.
    /// </remarks>
    private static void NameTheTrainCanonically<TIn, TOut>(
        ServiceTrain<TIn, TOut> serviceTrain,
        Exception failureReason
    )
    {
        if (
            failureReason.Data["TrainExceptionData"] is TrainExceptionData data
            && data.TrainExternalId == serviceTrain.ExternalId
        )
            data.TrainName = serviceTrain.TrainName;
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
