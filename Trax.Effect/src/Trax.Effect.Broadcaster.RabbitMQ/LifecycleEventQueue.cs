using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// The bounded, single-reader queue between <see cref="RabbitMqTrainEventBroadcaster.PublishAsync"/>
/// and its background sender. When it is full it gives up a junction event before a train's own
/// event, and a non-terminal train event before a terminal one, so a run whose <c>Started</c>
/// reached subscribers does not then lose its outcome, and a busy run's steps never crowd out
/// another run's lifecycle.
/// </summary>
/// <remarks>
/// Events rank lowest to highest: junction events (<c>JunctionStarted</c>, <c>Decided</c> and the
/// rest), then non-terminal ones (<c>Started</c>, <c>StateChanged</c>, <c>DataChanged</c>), then
/// terminal ones (<c>Completed</c>, <c>Failed</c>, <c>Cancelled</c>). A full queue drops an incoming
/// junction event. Any other incoming event takes the place of the oldest queued event of a lower
/// rank, and is dropped itself only when none is queued.
/// </remarks>
internal sealed class LifecycleEventQueue(int capacity)
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

    /// <summary>
    /// Queues <paramref name="message"/>, making room by the policy above when the queue is full.
    /// </summary>
    /// <param name="message">The event to queue.</param>
    /// <param name="dropped">
    /// The event given up to stay within capacity: <paramref name="message"/> itself, or the queued
    /// event it replaced. <c>null</c> when nothing was dropped.
    /// </param>
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

            var rank = Rank(message);

            // The lowest-ranked queued event below this one's rank goes first, oldest first.
            for (var below = 0; below < rank; below++)
            {
                for (var node = _items.First; node is not null; node = node.Next)
                {
                    if (Rank(node.Value) != below)
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

    /// <summary>
    /// Waits for the next event. Returns <c>null</c> once the queue is completed and empty.
    /// </summary>
    /// <param name="ct">Abandons the wait.</param>
    public async ValueTask<TrainLifecycleEventMessage?> ReadAsync(CancellationToken ct)
    {
        await _available.WaitAsync(ct);
        lock (_items)
        {
            if (_items.First is not { } first)
                return null; // the release Complete() made

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

    private static int Rank(TrainLifecycleEventMessage message) =>
        message.Junction is not null
        || TrainLifecycleEventMessage.IsJunctionEvent(message.EventType)
            ? 0
        : message.EventType is "Completed" or "Failed" or "Cancelled" ? 2
        : 1;
}
