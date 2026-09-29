namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// Configuration options for the RabbitMQ lifecycle event broadcaster.
/// </summary>
public class RabbitMqBroadcasterOptions
{
    /// <summary>
    /// AMQP connection URI (e.g., "amqp://guest:guest@localhost:5672").
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Name of the fanout exchange used for broadcasting lifecycle events.
    /// Defaults to "trax.lifecycle".
    /// </summary>
    public string ExchangeName { get; set; } = "trax.lifecycle";

    /// <summary>
    /// How many received events the receiver may hold unacknowledged at once. The broker stops
    /// delivering to this receiver until it acknowledges one, so a slow event handler leaves
    /// events queued on the broker rather than in the receiving process. Defaults to 64. Must be
    /// between 1 and 65535.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 64;
}
