namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// Strongly-typed client surface for <see cref="TraxTrainEventHub"/>.
/// Each connected client gets a <c>TrainEvent</c> callback whenever a train
/// lifecycle event passes the sink's filters.
/// </summary>
/// <remarks>
/// The payload is typed as <see cref="object"/> so the projection delegate inside
/// <c>SignalRSinkOptions</c> can produce any JSON-serializable shape without
/// forcing a generic parameter on the hub. Clients deserialize via
/// <c>connection.On&lt;TShape&gt;("TrainEvent", ...)</c>.
/// </remarks>
public interface ITraxTrainEventClient
{
    Task TrainEvent(object payload);
}
