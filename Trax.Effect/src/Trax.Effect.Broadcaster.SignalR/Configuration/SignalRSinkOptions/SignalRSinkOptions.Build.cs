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
            projection: _projection
        );
    }
}
