using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.SignalR.Configuration;

/// <summary>
/// Built, immutable configuration for the SignalR sink.
/// Produced by <see cref="SignalRSinkOptions.SignalRSinkOptions.Build"/> and registered as a singleton.
/// </summary>
public sealed class SignalRSinkConfiguration
{
    internal SignalRSinkConfiguration(
        IReadOnlySet<string> eventTypeFilter,
        IReadOnlySet<string> trainNameFilter,
        Func<TrainLifecycleEventMessage, object> projection
    )
    {
        EventTypeFilter = eventTypeFilter;
        TrainNameFilter = trainNameFilter;
        Projection = projection;
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
    /// Returns true if a message satisfies both the event-type and train-name filters.
    /// </summary>
    public bool Matches(TrainLifecycleEventMessage message)
    {
        if (EventTypeFilter.Count > 0 && !EventTypeFilter.Contains(message.EventType))
            return false;
        if (TrainNameFilter.Count > 0 && !TrainNameFilter.Contains(message.TrainName))
            return false;
        return true;
    }
}
