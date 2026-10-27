using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Broadcasting;

/// <summary>
/// Decides whether a received invalidation is let through: only a message delivered from
/// <paramref name="exchange"/>, the one the node subscribed to, and then, with a signing key, only
/// one whose signature matches and is fresh; without one (after <c>AllowUnsignedMessages()</c>),
/// any message from that exchange. A dropped message is logged as a warning at most once per
/// <see cref="RefusalWarningInterval"/>, with the count dropped since; the rest go to Debug.
/// </summary>
internal sealed class InvalidationMessageVerifier(
    string exchange,
    RabbitMqInvalidationOptions options,
    ILogger? logger,
    TimeProvider? time = null
)
{
    /// <summary>How often a dropped message is logged as a warning.</summary>
    internal static readonly TimeSpan RefusalWarningInterval = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _refusalLog = new();
    private DateTimeOffset _lastRefusalWarning = DateTimeOffset.MinValue;
    private int _refusalsSinceWarning;
    private long _refused;

    /// <summary>
    /// Messages dropped since this verifier was created, for their signature or for the exchange
    /// they came from.
    /// </summary>
    internal long Refused => Interlocked.Read(ref _refused);

    /// <summary>
    /// Whether the message, delivered from <paramref name="deliveredFrom"/>, may empty this node's
    /// caches.
    /// </summary>
    public bool Accept(string deliveredFrom, ReadOnlySpan<byte> body, string? signature)
    {
        // A queue can be reached by more than its binding (the default exchange routes to it by
        // name, and another exchange can be bound to it), so a delivery from anywhere else is
        // dropped before its signature is looked at.
        if (!string.Equals(deliveredFrom, exchange, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _refused);
            LogRefusal($"it came from exchange '{deliveredFrom}', not '{exchange}'", deliveredFrom);
            return false;
        }

        if (options.SigningKey is not { } key)
            return options.UnsignedMessagesAllowed;

        var verdict = InvalidationMessageSignature.Verify(
            key,
            deliveredFrom,
            body,
            signature,
            _time.GetUtcNow(),
            options.MaxClockSkew
        );
        if (verdict == InvalidationSignatureVerdict.Accepted)
            return true;

        Interlocked.Increment(ref _refused);
        LogRefusal($"signature {verdict}", deliveredFrom);
        return false;
    }

    private void LogRefusal(string reason, string deliveredFrom)
    {
        int suppressed;
        lock (_refusalLog)
        {
            var now = _time.GetUtcNow();
            if (now - _lastRefusalWarning < RefusalWarningInterval)
            {
                _refusalsSinceWarning++;
                logger?.LogDebug(
                    "Persisted-operation receiver dropped a message from exchange {Exchange}: {Reason}.",
                    deliveredFrom,
                    reason
                );
                return;
            }

            suppressed = _refusalsSinceWarning;
            _refusalsSinceWarning = 0;
            _lastRefusalWarning = now;
        }

        logger?.LogWarning(
            "Persisted-operation receiver dropped a message from exchange {Exchange}: {Reason}. "
                + "{Suppressed} more were dropped since the last warning. Every node and uploader "
                + "on the exchange must sign with the same SigningKey, and the clocks must agree "
                + "within MaxClockSkew.",
            deliveredFrom,
            reason,
            suppressed
        );
    }
}
