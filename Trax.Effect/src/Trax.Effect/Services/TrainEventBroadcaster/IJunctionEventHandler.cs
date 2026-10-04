namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Handles junction events: the steps of a run, published by a host that called
/// <c>AddJunctionEvents()</c>. Register implementations to forward them to a UI, such as a GraphQL
/// subscription or a SignalR hub.
/// </summary>
/// <remarks>
/// <para>Each message has one of the junction event types
/// (<see cref="TrainLifecycleEventMessage.IsJunctionEvent"/>) and a non-null
/// <see cref="TrainLifecycleEventMessage.Junction"/>. These never reach an
/// <see cref="ITrainEventHandler"/>, so a handler written for train events is not handed event types
/// it does not know.</para>
///
/// <para>A handler is called for both paths an event takes. In the host that runs the train it is
/// called on the run's path, right after the event is published, resolved from the run's scope; it
/// must return quickly and queue anything slow, because the next junction waits for it. In every
/// other host it is called by <see cref="TrainEventReceiverService"/> for events arriving over the
/// transport, resolved from a fresh scope per message, and an event this host published is not
/// delivered to it twice. Whatever it throws is logged and does not reach the run or the other
/// handlers.</para>
///
/// <para>The events carry the train's name so a handler can apply the same visibility rules it
/// applies to the train's own events (for example only trains marked <c>[TraxBroadcast]</c>, or only
/// callers allowed to see the train).</para>
/// </remarks>
public interface IJunctionEventHandler
{
    /// <summary>Handles one junction event.</summary>
    /// <param name="message">The event, with its step in <see cref="TrainLifecycleEventMessage.Junction"/>.</param>
    /// <param name="ct">The delivery token.</param>
    Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}
