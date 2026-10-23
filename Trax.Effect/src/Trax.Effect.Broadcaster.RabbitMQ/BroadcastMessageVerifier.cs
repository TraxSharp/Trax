using Microsoft.Extensions.Logging;

namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// Decides whether a received message is let through, by the posture in
/// <see cref="RabbitMqBroadcasterOptions"/>: with a signing key, only a message whose signature
/// matches and is fresh; without one (after <c>AllowUnsignedMessages()</c>), any message. A
/// dropped message is logged as a warning at most once per <see cref="RefusalWarningInterval"/>,
/// with the count dropped since; the rest go to Debug, so a flood cannot flood the log.
/// </summary>
internal sealed class BroadcastMessageVerifier(
    RabbitMqBroadcasterOptions options,
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

    /// <summary>Messages dropped for their signature since this verifier was created.</summary>
    internal long Refused => Interlocked.Read(ref _refused);

    /// <summary>Whether the message may reach the handler.</summary>
    public bool Accept(string exchange, ReadOnlySpan<byte> body, string? signature)
    {
        if (options.SigningKey is not { } key)
            return options.UnsignedMessagesAllowed;

        var verdict = BroadcastMessageSignature.Verify(
            key,
            exchange,
            body,
            signature,
            _time.GetUtcNow(),
            options.MaxClockSkew
        );
        if (verdict == BroadcastSignatureVerdict.Accepted)
            return true;

        Interlocked.Increment(ref _refused);
        LogRefusal(verdict, exchange);
        return false;
    }

    private void LogRefusal(BroadcastSignatureVerdict verdict, string exchange)
    {
        int suppressed;
        lock (_refusalLog)
        {
            var now = _time.GetUtcNow();
            if (now - _lastRefusalWarning < RefusalWarningInterval)
            {
                _refusalsSinceWarning++;
                logger?.LogDebug(
                    "RabbitMQ receiver dropped a message from exchange {Exchange}: signature {Verdict}.",
                    exchange,
                    verdict
                );
                return;
            }

            suppressed = _refusalsSinceWarning;
            _refusalsSinceWarning = 0;
            _lastRefusalWarning = now;
        }

        logger?.LogWarning(
            "RabbitMQ receiver dropped a message from exchange {Exchange}: signature {Verdict}. "
                + "{Suppressed} more were dropped since the last warning. Every publisher on the "
                + "exchange must sign with the same SigningKey, and the clocks must agree within "
                + "MaxClockSkew.",
            exchange,
            verdict,
            suppressed
        );
    }
}
