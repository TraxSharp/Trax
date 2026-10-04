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
    /// Name of the fanout exchange junction events (each step of a run, from a host that called
    /// <c>AddJunctionEvents()</c>) are published to. Defaults to <see cref="ExchangeName"/> followed
    /// by <c>".junctions"</c>.
    /// </summary>
    /// <remarks>
    /// Junction events have an exchange of their own so that a receiver bound only to
    /// <see cref="ExchangeName"/>, such as a host running a Trax version from before junction
    /// events, never receives one and never mistakes one for a train event. A receiver of this
    /// version binds its queue to both. Set it only to give the junction exchange another name;
    /// it must differ from <see cref="ExchangeName"/>.
    /// </remarks>
    public string? JunctionExchangeName { get; set; }

    /// <summary>The junction exchange in effect: <see cref="JunctionExchangeName"/> or its default.</summary>
    internal string EffectiveJunctionExchangeName =>
        JunctionExchangeName ?? $"{ExchangeName}.junctions";

    /// <summary>
    /// How many received events the receiver may hold unacknowledged at once. The broker stops
    /// delivering to this receiver until it acknowledges one, so a slow event handler leaves
    /// events queued on the broker rather than in the receiving process. Defaults to 64. Must be
    /// between 1 and 65535.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 64;
}
