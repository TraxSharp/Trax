namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// Configuration options for the RabbitMQ lifecycle event broadcaster.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/use-rabbit-mq">SDK reference</seealso>
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

    /// <summary>
    /// The key every process on the exchange shares, at least
    /// <see cref="MinimumSigningKeyLength"/> bytes. The broadcaster signs each message it publishes
    /// with it (HMAC-SHA256 over the body, the exchange, a timestamp and a message id), and the
    /// receiver drops any message whose signature is missing, does not match, or is older than
    /// <see cref="MaxClockSkew"/>. Required unless <see cref="AllowUnsignedMessages"/> is called.
    /// </summary>
    public byte[]? SigningKey { get; set; }

    /// <summary>The shortest <see cref="SigningKey"/> accepted, in bytes: 32.</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>
    /// How far a signed message's timestamp may be from the receiving process's clock, either way,
    /// before the message is dropped. Default five minutes. Must be positive.
    /// </summary>
    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Whether <see cref="AllowUnsignedMessages"/> was called.</summary>
    public bool UnsignedMessagesAllowed { get; private set; }

    /// <summary>
    /// Publishes messages unsigned and accepts messages that carry no signature, when no
    /// <see cref="SigningKey"/> is set. Anyone who can publish to the exchange can then put any
    /// event in front of every receiver, so the receiver logs a warning when it starts. Suitable
    /// only for a broker no one else can publish to. With a <see cref="SigningKey"/> set this has
    /// no effect: every message is signed and checked.
    /// </summary>
    /// <returns>These options, for chaining.</returns>
    public RabbitMqBroadcasterOptions AllowUnsignedMessages()
    {
        UnsignedMessagesAllowed = true;
        return this;
    }

    /// <summary>
    /// Accepts a <see cref="ConnectionString"/> that is not <c>amqps://</c> for a broker that is not
    /// on this machine. Without it, a plain <c>amqp://</c> URI is refused unless its host is a
    /// loopback address, because the broker credentials in it and every event would cross the
    /// network in cleartext. Set it only when the network to the broker is trusted.
    /// </summary>
    public bool AllowInsecureTransport { get; set; }

    /// <summary>
    /// What is wrong with these options for connecting, or null when nothing is. Checked by
    /// <c>UseRabbitMq</c>, by the broadcaster when it is created, and by the receiver when it starts.
    /// </summary>
    internal string? Problem()
    {
        if (SigningKey is null)
        {
            if (!UnsignedMessagesAllowed)
                return "RabbitMqBroadcasterOptions requires a SigningKey of at least "
                    + $"{MinimumSigningKeyLength} bytes, shared by every process on the exchange "
                    + "(for example, 32 random bytes, base64-encoded in configuration), or an "
                    + "explicit AllowUnsignedMessages() for a broker no one else can publish to.";
        }
        else if (SigningKey.Length < MinimumSigningKeyLength)
        {
            return $"RabbitMqBroadcasterOptions.SigningKey must be at least {MinimumSigningKeyLength} "
                + "bytes (for example, 32 random bytes, base64-encoded in configuration).";
        }

        if (MaxClockSkew <= TimeSpan.Zero)
            return "RabbitMqBroadcasterOptions.MaxClockSkew must be positive.";

        if (!AllowInsecureTransport && IsCleartextToAnotherHost(out var host))
            return $"The RabbitMQ broker {host} is not reached over TLS, so its credentials and "
                + "every event would cross the network in cleartext. Use an amqps:// URI, a "
                + "loopback host, or set RabbitMqBroadcasterOptions.AllowInsecureTransport when the "
                + "network to the broker is trusted.";

        return null;
    }

    /// <summary>Whether <see cref="AllowInsecureTransport"/> is what lets this connection through.</summary>
    internal bool InsecureTransportInUse =>
        AllowInsecureTransport && IsCleartextToAnotherHost(out _);

    // Plain amqp:// to a host that is not this machine (localhost, 127.0.0.0/8, ::1).
    private bool IsCleartextToAnotherHost(out string host)
    {
        host = "";
        if (!Uri.TryCreate(ConnectionString, UriKind.Absolute, out var uri))
            return false;
        host = uri.Host;
        return !string.Equals(uri.Scheme, "amqps", StringComparison.OrdinalIgnoreCase)
            && !uri.IsLoopback;
    }
}
