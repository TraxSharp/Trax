using Trax.Effect.Broadcaster.SignalR.Configuration;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// The bounded, single-reader queue between the dispatcher and its background sender. When it is
/// full it gives up a junction event before a train's own event, so a burst of one run's steps
/// never costs another run its <c>Completed</c> or <c>Failed</c>.
/// </summary>
/// <remarks>
/// A full queue drops an incoming junction event. An incoming train event takes the place of the
/// oldest queued junction event, and is dropped itself only when none is queued.
/// </remarks>
internal sealed class DeliveryQueue(int capacity)
{
    private readonly LinkedList<TrainLifecycleEventMessage> _items = [];
    private readonly SemaphoreSlim _available = new(0);
    private bool _completed;

    /// <summary>How many events are waiting for the sender.</summary>
    public int Count
    {
        get
        {
            lock (_items)
                return _items.Count;
        }
    }

    /// <summary>Queues <paramref name="message"/>, making room by the policy above when full.</summary>
    /// <param name="message">The event to queue.</param>
    /// <param name="dropped">The event given up, or <c>null</c> when nothing was dropped.</param>
    /// <returns><c>false</c> only when the queue was completed; nothing is dropped then.</returns>
    public bool TryWrite(
        TrainLifecycleEventMessage message,
        out TrainLifecycleEventMessage? dropped
    )
    {
        dropped = null;
        lock (_items)
        {
            if (_completed)
                return false;

            if (_items.Count < capacity)
            {
                _items.AddLast(message);
                _available.Release();
                return true;
            }

            if (!SignalRSinkConfiguration.IsJunctionEvent(message))
            {
                for (var node = _items.First; node is not null; node = node.Next)
                {
                    if (!SignalRSinkConfiguration.IsJunctionEvent(node.Value))
                        continue;

                    // One out, one in: the count the sender waits on is unchanged.
                    dropped = node.Value;
                    _items.Remove(node);
                    _items.AddLast(message);
                    return true;
                }
            }

            dropped = message;
            return true;
        }
    }

    /// <summary>Waits for the next event; <c>null</c> once the queue is completed and empty.</summary>
    public async ValueTask<TrainLifecycleEventMessage?> ReadAsync(CancellationToken ct)
    {
        await _available.WaitAsync(ct);
        lock (_items)
        {
            if (_items.First is not { } first)
                return null;

            _items.RemoveFirst();
            return first.Value;
        }
    }

    /// <summary>Stops accepting events; the reader drains what is queued, then gets <c>null</c>.</summary>
    public void Complete()
    {
        lock (_items)
        {
            if (_completed)
                return;
            _completed = true;
            _available.Release();
        }
    }
}
