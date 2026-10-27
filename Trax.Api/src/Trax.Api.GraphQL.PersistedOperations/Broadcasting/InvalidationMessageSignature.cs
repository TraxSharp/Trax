using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Trax.Api.GraphQL.PersistedOperations.Broadcasting;

/// <summary>What <see cref="InvalidationMessageSignature.Verify"/> found.</summary>
internal enum InvalidationSignatureVerdict
{
    /// <summary>The signature matches and its timestamp is within the allowed skew.</summary>
    Accepted,

    /// <summary>The message carries no signature.</summary>
    Missing,

    /// <summary>The signature is malformed or does not match the message.</summary>
    Invalid,

    /// <summary>The signature matches, but its timestamp is further from this clock than allowed.</summary>
    Stale,
}

/// <summary>
/// The signature each invalidation carries, in the <see cref="Header"/> header:
/// <c>v1,t=&lt;unix seconds&gt;,n=&lt;message id&gt;,s=&lt;base64 HMAC-SHA256&gt;</c>. The MAC covers
/// a domain tag, the version, the timestamp, the message id, the exchange the message is published
/// to and the body, so a body moved to another exchange, or given another timestamp, no longer
/// matches.
/// </summary>
/// <remarks>
/// The header has the RabbitMQ train-event broadcaster's format
/// (<c>Trax.Effect.Broadcaster.RabbitMQ</c>), but the MAC starts with its own tag,
/// <see cref="DomainTag"/>, so a message signed for one of the two never verifies as the other's,
/// even when a host gives both the same key.
/// </remarks>
internal static class InvalidationMessageSignature
{
    /// <summary>The message header the signature travels in.</summary>
    internal const string Header = "trax-signature";

    private const string Version = "v1";

    /// <summary>What every MAC starts with, distinct from the train-event broadcaster's.</summary>
    internal const string DomainTag = "trax-invalidation/" + Version;
    private const int MessageIdBytes = 16;

    /// <summary>Signs <paramref name="body"/> for <paramref name="exchange"/> at <paramref name="now"/>.</summary>
    public static string Create(
        byte[] key,
        string exchange,
        ReadOnlySpan<byte> body,
        DateTimeOffset now
    ) =>
        Create(
            key,
            exchange,
            body,
            now.ToUnixTimeSeconds(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(MessageIdBytes))
        );

    internal static string Create(
        byte[] key,
        string exchange,
        ReadOnlySpan<byte> body,
        long timestamp,
        string messageId
    )
    {
        var mac = ComputeMac(key, exchange, body, timestamp, messageId);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Version},t={timestamp},n={messageId},s={Convert.ToBase64String(mac)}"
        );
    }

    /// <summary>
    /// Checks <paramref name="signature"/> against the message. The MAC is compared in constant
    /// time, and the timestamp is judged only once the MAC matches.
    /// </summary>
    public static InvalidationSignatureVerdict Verify(
        byte[] key,
        string exchange,
        ReadOnlySpan<byte> body,
        string? signature,
        DateTimeOffset now,
        TimeSpan maxClockSkew
    )
    {
        if (string.IsNullOrEmpty(signature))
            return InvalidationSignatureVerdict.Missing;

        if (!TryParse(signature, out var timestamp, out var messageId, out var mac))
            return InvalidationSignatureVerdict.Invalid;

        var expected = ComputeMac(key, exchange, body, timestamp, messageId);
        if (!CryptographicOperations.FixedTimeEquals(expected, mac))
            return InvalidationSignatureVerdict.Invalid;

        var age = now.ToUnixTimeSeconds() - timestamp;
        return Math.Abs(age) > (long)maxClockSkew.TotalSeconds
            ? InvalidationSignatureVerdict.Stale
            : InvalidationSignatureVerdict.Accepted;
    }

    /// <summary>Reads the signature header, which the client library hands over as UTF-8 bytes.</summary>
    public static string? Read(IDictionary<string, object?>? headers) =>
        headers is not null && headers.TryGetValue(Header, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null,
            }
            : null;

    private static bool TryParse(
        string signature,
        out long timestamp,
        out string messageId,
        out byte[] mac
    )
    {
        timestamp = 0;
        messageId = "";
        mac = [];

        var parts = signature.Split(',');
        if (
            parts.Length != 4
            || parts[0] != Version
            || !parts[1].StartsWith("t=", StringComparison.Ordinal)
            || !parts[2].StartsWith("n=", StringComparison.Ordinal)
            || !parts[3].StartsWith("s=", StringComparison.Ordinal)
        )
            return false;

        if (
            !long.TryParse(
                parts[1].AsSpan(2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out timestamp
            )
        )
            return false;

        messageId = parts[2][2..];
        if (messageId.Length != MessageIdBytes * 2 || !messageId.All(char.IsAsciiHexDigitUpper))
            return false;

        var encoded = parts[3][2..];
        if (!Base64.IsValid(encoded))
            return false;
        mac = Convert.FromBase64String(encoded);
        return mac.Length == HMACSHA256.HashSizeInBytes;
    }

    // Each variable field is length-prefixed, so no two different messages share an input.
    private static byte[] ComputeMac(
        byte[] key,
        string exchange,
        ReadOnlySpan<byte> body,
        long timestamp,
        string messageId
    )
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        var exchangeBytes = Encoding.UTF8.GetBytes(exchange);
        hmac.AppendData(
            Encoding.UTF8.GetBytes(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DomainTag}\n{timestamp}\n{messageId}\n{exchangeBytes.Length}\n"
                )
            )
        );
        hmac.AppendData(exchangeBytes);
        hmac.AppendData("\n"u8);
        hmac.AppendData(body);
        return hmac.GetHashAndReset();
    }
}
