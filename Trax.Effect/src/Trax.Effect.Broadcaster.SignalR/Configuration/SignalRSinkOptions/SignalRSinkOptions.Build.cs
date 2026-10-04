using Trax.Effect.Broadcaster.SignalR.Services;

namespace Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;

public partial class SignalRSinkOptions
{
    /// <summary>
    /// Produces the immutable <see cref="SignalRSinkConfiguration"/> registered in DI.
    /// </summary>
    internal SignalRSinkConfiguration Build()
    {
        return new SignalRSinkConfiguration(
            eventTypeFilter: _eventTypes,
            trainNameFilter: _trainNames,
            projection: _projection,
            deliveryQueueCapacity: _deliveryQueueCapacity,
            junctionEvents: _junctionEvents,
            // A host's own projection wins over WithJunctionAnswers, whichever was called last.
            junctionProjection: _junctionProjection
                ?? (
                    _junctionAnswers
                        ? message =>
                            DefaultTraxJunctionClientEventProjection.Project(message, answers: true)
                        : null
                )
        );
    }
}
