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
}
