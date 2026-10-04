using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Services.JunctionEvents;

/// <summary>
/// Builds and publishes the start and end of a junction for <c>EffectJunction</c>.
/// </summary>
internal static class JunctionSteps
{
    /// <summary>Publishes a junction's start and returns its position in the run.</summary>
    public static async Task<int> Started(JunctionEventRun run, string name, DateTime startedAt)
    {
        var position = run.NextPosition();

        await run.Publish(
            TrainLifecycleEventMessage.JunctionStartedEventType,
            run.OnTrack(
                new JunctionEventPayload(
                    position,
                    JunctionRunKind.Junction,
                    name,
                    JunctionRunState.InProgress,
                    startedAt
                )
            )
        );

        return position;
    }

    /// <summary>
    /// Publishes a junction's end: completed when <paramref name="failure"/> is null, cancelled when
    /// it is a cancellation the run was asked for, failed otherwise. Only the failure's type and
    /// class are published, never its message.
    /// </summary>
    public static Task Ended(
        JunctionEventRun run,
        int position,
        string name,
        DateTime startedAt,
        DateTime endedAt,
        Exception? failure,
        bool requestedCancellation,
        IServiceProvider? services,
        ILogger? logger
    )
    {
        var duration = (endedAt - startedAt).TotalMilliseconds;

        if (failure is null)
            return run.Publish(
                TrainLifecycleEventMessage.JunctionCompletedEventType,
                run.OnTrack(
                    new JunctionEventPayload(
                        position,
                        JunctionRunKind.Junction,
                        name,
                        JunctionRunState.Completed,
                        startedAt,
                        endedAt,
                        duration
                    )
                )
            );

        var type = ExceptionType(failure);

        if (requestedCancellation)
            return run.Publish(
                TrainLifecycleEventMessage.JunctionCancelledEventType,
                run.OnTrack(
                    new JunctionEventPayload(
                        position,
                        JunctionRunKind.Junction,
                        name,
                        JunctionRunState.Cancelled,
                        startedAt,
                        endedAt,
                        duration,
                        FailureException: type
                    )
                )
            );

        return run.Publish(
            TrainLifecycleEventMessage.JunctionFailedEventType,
            run.OnTrack(
                new JunctionEventPayload(
                    position,
                    JunctionRunKind.Junction,
                    name,
                    JunctionRunState.Failed,
                    startedAt,
                    endedAt,
                    duration,
                    FailureClass: Classify(failure, services, logger),
                    FailureException: type
                )
            )
        );
    }

    /// <summary>The exception's type name, as the run would record it in <c>FailureException</c>.</summary>
    private static string ExceptionType(Exception failure) =>
        failure.Data["TrainExceptionData"] is TrainExceptionData { Type: { } type }
            ? type
            : failure.GetType().Name;

    /// <summary>
    /// The failure's class, decided as the run's own would be, without writing anything onto the
    /// exception: the class it carries, else the registered classifier's, else transient for a
    /// cancellation nothing asked for, else unclassified.
    /// </summary>
    private static FailureClass Classify(
        Exception failure,
        IServiceProvider? services,
        ILogger? logger
    )
    {
        if (failure.Data["TrainExceptionData"] is TrainExceptionData { FailureClass: { } carried })
            return Enum.IsDefined(carried) ? carried : FailureClass.Unclassified;

        FailureClass? answer = null;
        try
        {
            if (services?.GetService(typeof(IFailureClassifier)) is IFailureClassifier classifier)
                answer = classifier.Classify(failure);
        }
        catch (Exception e)
        {
            logger?.LogDebug(e, "Failure classifier threw while classifying a junction's failure.");
        }

        if (answer is { } value && Enum.IsDefined(value) && value != FailureClass.Unclassified)
            return value;

        return failure is OperationCanceledException
            ? FailureClass.Transient
            : FailureClass.Unclassified;
    }
}
