using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.DTOs;

/// <summary>
/// What happened to one step of a run: the payload of the <c>onJunctionEvent</c> subscription.
/// </summary>
/// <remarks>
/// Published only by a host that called <c>AddJunctionEvents()</c>, and only for the trains whose
/// train events are published: <c>[TraxBroadcast]</c> trains, or every train on a host exposing the
/// operations surface. It carries no train input, output or failure message.
/// </remarks>
/// <param name="MetadataId">The run's execution id.</param>
/// <param name="ExternalId">The run's stable external id.</param>
/// <param name="TrainName">The train's canonical name (its interface's full name).</param>
/// <param name="EventType">What happened to the step.</param>
/// <param name="Timestamp">When the event was published (UTC).</param>
/// <param name="Junction">The step.</param>
public sealed record JunctionEvent(
    long MetadataId,
    string ExternalId,
    string TrainName,
    JunctionEventType EventType,
    DateTime Timestamp,
    JunctionStep Junction
)
{
    /// <summary>
    /// This event's position in the subscription that delivered it: 1 for the first, one more for
    /// each after it, and two more when events were lost in between. 0 outside a subscription.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// The number the publisher gave this event on its topic, or 0 when it was sent without one.
    /// Not part of the GraphQL type; the subscription reports a loss through <see cref="Sequence"/>.
    /// </summary>
    internal long PublishSequence { get; init; }

    /// <summary>
    /// The event a broadcast junction message describes, or <c>null</c> when it is not a junction
    /// event this API knows.
    /// </summary>
    /// <param name="message">A message whose event type is a junction event type.</param>
    internal static JunctionEvent? From(TrainLifecycleEventMessage message)
    {
        if (message.Junction is not { } step)
            return null;

        JunctionEventType? type = message.EventType switch
        {
            TrainLifecycleEventMessage.JunctionStartedEventType =>
                JunctionEventType.JunctionStarted,
            TrainLifecycleEventMessage.JunctionCompletedEventType =>
                JunctionEventType.JunctionCompleted,
            TrainLifecycleEventMessage.JunctionFailedEventType => JunctionEventType.JunctionFailed,
            TrainLifecycleEventMessage.JunctionCancelledEventType =>
                JunctionEventType.JunctionCancelled,
            TrainLifecycleEventMessage.DecidedEventType => JunctionEventType.Decided,
            TrainLifecycleEventMessage.DecisionRefusedEventType =>
                JunctionEventType.DecisionRefused,
            TrainLifecycleEventMessage.RoutedEventType => JunctionEventType.Routed,
            _ => null,
        };

        return type is null
            ? null
            : new JunctionEvent(
                message.MetadataId,
                message.ExternalId,
                message.TrainName,
                type.Value,
                message.Timestamp,
                JunctionStep.From(step)
            );
    }
}

/// <summary>What happened to a step of a run.</summary>
public enum JunctionEventType
{
    /// <summary>A junction started.</summary>
    JunctionStarted,

    /// <summary>A junction returned a result.</summary>
    JunctionCompleted,

    /// <summary>A junction failed.</summary>
    JunctionFailed,

    /// <summary>A junction was stopped by a cancellation the run was asked for.</summary>
    JunctionCancelled,

    /// <summary>A routing step asked a question and the run acts on the answer.</summary>
    Decided,

    /// <summary>A decider's answer the run would not act on; the routing step fails.</summary>
    DecisionRefused,

    /// <summary>A routing step sent the run down a track.</summary>
    Routed,
}
