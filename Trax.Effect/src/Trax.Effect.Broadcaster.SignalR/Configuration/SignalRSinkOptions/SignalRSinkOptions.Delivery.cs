namespace Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;

public partial class SignalRSinkOptions
{
    /// <summary>
    /// Sets how many events may wait for delivery to clients (default
    /// <see cref="DefaultDeliveryQueueCapacity"/>). A train's lifecycle hook only queues the
    /// event; one background sender delivers the queue in order. When clients fall behind and
    /// the queue is full, further events are dropped and logged instead of holding up trains.
    /// </summary>
    public SignalRSinkOptions WithDeliveryQueueCapacity(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "WithDeliveryQueueCapacity() requires a capacity of at least 1. "
                    + $"Omit the call to use the default of {DefaultDeliveryQueueCapacity}."
            );
        }

        _deliveryQueueCapacity = capacity;
        return this;
    }
}
