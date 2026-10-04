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
    /// <summary>
    /// Delivers one lifecycle event to the client, under the method name <c>"TrainEvent"</c>.
    /// </summary>
    /// <param name="payload">
    /// The event as shaped by the sink's projection: a <c>TraxClientEvent</c> unless
    /// <c>WithProjection</c> replaced it.
    /// </param>
    /// <returns>A task that completes when SignalR has handed the message to the transport.</returns>
    Task TrainEvent(object payload);

    /// <summary>
    /// Delivers one junction event (a step of a run) to the client, under the method name
    /// <c>"JunctionEvent"</c>. Sent only by a sink configured with <c>WithJunctionEvents()</c>.
    /// </summary>
    /// <param name="payload">The step as a <c>TraxJunctionClientEvent</c>.</param>
    /// <returns>A task that completes when SignalR has handed the message to the transport.</returns>
    Task JunctionEvent(object payload);
}
