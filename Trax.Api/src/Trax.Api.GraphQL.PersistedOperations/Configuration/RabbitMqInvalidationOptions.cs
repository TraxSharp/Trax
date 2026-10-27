namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

/// <summary>
/// How invalidations travel over RabbitMQ, configured by the callback of
/// <c>UseRabbitMqInvalidation(connectionString, configure)</c>: the key each message is signed
/// with, and whether a broker that is not reached over TLS is accepted.
/// </summary>
/// <remarks>
/// A message on the invalidation exchange empties a node's persisted-operation caches, so whoever
/// can publish one can make every node read its documents again. Every node and every uploader
/// signs what it publishes with <see cref="SigningKey"/>, and a node drops a message that is not
/// signed with it. The signature header has the RabbitMQ train-event broadcaster's format, and its
/// MAC is tagged for invalidations, so neither verifies as the other.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/persisted-operations/persisted-operations-builder">SDK reference</seealso>
public sealed class RabbitMqInvalidationOptions
{
    /// <summary>The shortest <see cref="SigningKey"/> accepted, in bytes: 32.</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>
    /// The key every process on the invalidation exchange shares, at least
    /// <see cref="MinimumSigningKeyLength"/> bytes. Each change is published signed with it
    /// (HMAC-SHA256 over the body, the exchange, a timestamp and a message id), and a node drops a
    /// message whose signature is missing, does not match, or is further than
    /// <see cref="MaxClockSkew"/> from its clock. Required unless
    /// <see cref="AllowUnsignedMessages"/> is called.
    /// </summary>
    public byte[]? SigningKey { get; set; }

    /// <summary>
    /// How far a signed message's timestamp may be from the receiving node's clock, either way,
    /// before the message is dropped. Default five minutes. Must be positive and at most an hour:
    /// a longer window would let a captured message be replayed for that long.
    /// </summary>
    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The longest <see cref="MaxClockSkew"/> accepted: one hour.</summary>
    internal static readonly TimeSpan LongestClockSkew = TimeSpan.FromHours(1);

    /// <summary>Whether <see cref="AllowUnsignedMessages"/> was called.</summary>
    public bool UnsignedMessagesAllowed { get; private set; }

    /// <summary>
    /// Publishes changes unsigned and accepts messages that carry no signature, when no
    /// <see cref="SigningKey"/> is set. Anyone who can publish to the exchange can then empty every
    /// node's caches whenever they like, so the receiver logs a warning when it starts. Suitable
    /// only for a broker no one else can publish to. With a <see cref="SigningKey"/> set this has
    /// no effect.
    /// </summary>
    /// <returns>These options, for chaining.</returns>
    public RabbitMqInvalidationOptions AllowUnsignedMessages()
    {
        UnsignedMessagesAllowed = true;
        return this;
    }

    /// <summary>
    /// Accepts a connection string that is not <c>amqps://</c> for a broker that is not on this
    /// machine. Without it, a plain <c>amqp://</c> URI is refused unless its host is a loopback
    /// address, because the broker credentials in it and every message would cross the network in
    /// cleartext. Set it only when the network to the broker is trusted.
    /// </summary>
    public bool AllowInsecureTransport { get; set; }

    /// <summary>
    /// What is wrong with these options for <paramref name="connectionString"/>, or null when
    /// nothing is. Checked when the builder is built, by the broadcaster when it is created, and by
    /// the receiver when it starts.
    /// </summary>
    internal string? Problem(string? connectionString)
    {
        if (SigningKey is null)
        {
            if (!UnsignedMessagesAllowed)
                return "UseRabbitMqInvalidation requires a SigningKey of at least "
                    + $"{MinimumSigningKeyLength} bytes, shared by every node and uploader on the "
                    + "exchange (for example, 32 random bytes, base64-encoded in configuration), or "
                    + "an explicit AllowUnsignedMessages() for a broker no one else can publish to.";
        }
        else if (SigningKey.Length < MinimumSigningKeyLength)
        {
            return $"RabbitMqInvalidationOptions.SigningKey must be at least {MinimumSigningKeyLength} "
                + "bytes (for example, 32 random bytes, base64-encoded in configuration).";
        }

        if (MaxClockSkew <= TimeSpan.Zero)
            return "RabbitMqInvalidationOptions.MaxClockSkew must be positive.";

        if (MaxClockSkew > LongestClockSkew)
            return "RabbitMqInvalidationOptions.MaxClockSkew must be at most one hour: a signed "
                + "message is accepted for that long either side of the node's clock.";

        if (!AllowInsecureTransport && IsCleartextToAnotherHost(connectionString, out var host))
            return $"The RabbitMQ broker {host} is not reached over TLS, so its credentials and "
                + "every invalidation would cross the network in cleartext. Use an amqps:// URI, a "
                + "loopback host, or set RabbitMqInvalidationOptions.AllowInsecureTransport when the "
                + "network to the broker is trusted.";

        return null;
    }

    /// <summary>Whether <see cref="AllowInsecureTransport"/> is what lets this connection through.</summary>
    internal bool InsecureTransportInUse(string? connectionString) =>
        AllowInsecureTransport && IsCleartextToAnotherHost(connectionString, out _);

    // Plain amqp:// to a host that is not this machine (localhost, 127.0.0.0/8, ::1).
    private static bool IsCleartextToAnotherHost(string? connectionString, out string host)
    {
        host = "";
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            return false;
        host = uri.Host;
        return !string.Equals(uri.Scheme, "amqps", StringComparison.OrdinalIgnoreCase)
            && !uri.IsLoopback;
    }
}
