using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.SignalR.Configuration;

/// <summary>
/// Built, immutable configuration for the SignalR sink.
/// Produced by <see cref="SignalRSinkOptions.SignalRSinkOptions.Build"/> and registered as a singleton.
/// </summary>
internal sealed class SignalRSinkConfiguration
{
    internal SignalRSinkConfiguration(
        IReadOnlySet<string> eventTypeFilter,
        IReadOnlySet<string> trainNameFilter,
        Func<TrainLifecycleEventMessage, object> projection,
        int deliveryQueueCapacity,
        bool junctionEvents = false,
        Func<TrainLifecycleEventMessage, object>? junctionProjection = null
    )
    {
        JunctionEvents = junctionEvents;
        JunctionProjection =
            junctionProjection
            ?? (
                message => DefaultTraxJunctionClientEventProjection.Project(message, answers: false)
            );
        EventTypeFilter = eventTypeFilter;
        TrainNameFilter = trainNameFilter;
        Projection = projection;
        DeliveryQueueCapacity = deliveryQueueCapacity;
    }

    /// <summary>
    /// Allowed event types (e.g. "Completed"). Empty set means allow-all.
    /// </summary>
    public IReadOnlySet<string> EventTypeFilter { get; }

    /// <summary>
    /// Allowed train interface FullNames. Empty set means allow-all.
    /// </summary>
    public IReadOnlySet<string> TrainNameFilter { get; }

    /// <summary>
    /// Projection applied to each message before it is sent to clients.
    /// </summary>
    public Func<TrainLifecycleEventMessage, object> Projection { get; }

    /// <summary>
    /// How many events may wait for delivery to clients. When the queue is full, further events
    /// are dropped rather than holding up the train that raised them.
    /// </summary>
    public int DeliveryQueueCapacity { get; }

    /// <summary>
    /// Whether junction events are sent to clients. Off unless <c>WithJunctionEvents()</c> was called.
    /// </summary>
    public bool JunctionEvents { get; }

    /// <summary>
    /// Projection applied to each junction event before it is sent to clients. By default a
    /// <c>TraxJunctionClientEvent</c> without the answer or the confidence.
    /// </summary>
    public Func<TrainLifecycleEventMessage, object> JunctionProjection { get; }

    /// <summary>
    /// Returns true if a message satisfies both the event-type and train-name filters. A junction
    /// event also needs <see cref="JunctionEvents"/>, so a sink that did not ask for them never
    /// sends one, whatever the filters allow.
    /// </summary>
    public bool Matches(TrainLifecycleEventMessage message)
    {
        if (IsJunctionEvent(message) && !JunctionEvents)
            return false;
        if (EventTypeFilter.Count > 0 && !EventTypeFilter.Contains(message.EventType))
            return false;
        if (TrainNameFilter.Count > 0 && !TrainNameFilter.Contains(message.TrainName))
            return false;
        return true;
    }

    /// <summary>Whether the message is a junction event rather than one of the train's own.</summary>
    public static bool IsJunctionEvent(TrainLifecycleEventMessage message) =>
        message.Junction is not null
        || TrainLifecycleEventMessage.IsJunctionEvent(message.EventType);
}
