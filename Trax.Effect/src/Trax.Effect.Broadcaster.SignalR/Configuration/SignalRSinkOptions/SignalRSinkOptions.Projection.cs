using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;

public partial class SignalRSinkOptions
{
    /// <summary>
    /// Replace the default <c>TraxClientEvent</c> projection. The supplied function is invoked
    /// for every event that passes the filters and produces the payload sent to clients.
    /// </summary>
    /// <typeparam name="TClient">The shape sent to SignalR clients. Must be JSON-serializable.</typeparam>
    public SignalRSinkOptions WithProjection<TClient>(
        Func<TrainLifecycleEventMessage, TClient> projection
    )
        where TClient : notnull
    {
        if (projection is null)
        {
            throw new ArgumentNullException(
                nameof(projection),
                "WithProjection() requires a non-null projection. "
                    + "Omit the call to use the default TraxClientEvent projection."
            );
        }

        _projection = message => projection(message);
        return this;
    }
}
