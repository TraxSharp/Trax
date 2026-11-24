using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// The HMAC a hub puts on a request to the sandbox launcher, and the one the launcher puts on its
/// answer.
/// </summary>
/// <remarks>
/// <para>
/// A request carries <c>v1,t=&lt;unix seconds&gt;,n=&lt;nonce&gt;,s=&lt;base64 HMAC-SHA256&gt;</c> in
/// <see cref="HeaderName"/>, over the timestamp, the nonce and the exact body. An answer carries
/// <c>v1,s=&lt;base64&gt;</c> in <see cref="ResponseHeaderName"/>, over the request's nonce and
/// the exact answer body. Each has its own prefix, which also differs from the runner signature's,
/// so no signature made for one can be presented as another.
/// </para>
/// <para>
/// Every answer is signed, refusals included, so a hub reads nothing the launcher did not write
/// for that request, whatever the status code or the network in between.
/// </para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#the-wire">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SandboxChannelSignature
{
    /// <summary>The request header.</summary>
    public const string HeaderName = "Trax-Sandbox-Signature";

    /// <summary>The answer header.</summary>
    public const string ResponseHeaderName = "Trax-Sandbox-Response-Signature";

    /// <summary>The shortest key accepted, in bytes.</summary>
    public const int MinimumKeyLength = 32;

    private const string Version = "v1";

    /// <summary>Throws when <paramref name="key"/> is missing or shorter than <see cref="MinimumKeyLength"/>.</summary>
    public static void EnsureKey(byte[]? key, string paramName)
    {
        if (key is null || key.Length < MinimumKeyLength)
            throw new ArgumentException(
                $"A sandbox launcher key must be at least {MinimumKeyLength} bytes "
                    + "(32 random bytes, base64-encoded in configuration).",
                paramName
            );
    }

    /// <summary>Signs a request body with a fresh nonce and the time from <paramref name="time"/>.</summary>
    public static string Sign(byte[] key, ReadOnlySpan<byte> body, TimeProvider time)
    {
        EnsureKey(key, nameof(key));
        ArgumentNullException.ThrowIfNull(time);
        return Sign(
            key,
            body,
            time.GetUtcNow().ToUnixTimeSeconds(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
        );
    }

    /// <summary>Signs a request body with the timestamp and nonce given; for tests and replays of a known value.</summary>
    public static string Sign(byte[] key, ReadOnlySpan<byte> body, long timestamp, string nonce)
    {
        EnsureKey(key, nameof(key));
        var mac = RequestMac(key, body, timestamp, nonce);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Version},t={timestamp},n={nonce},s={Convert.ToBase64String(mac)}"
        );
    }

    /// <summary>
    /// Whether <paramref name="signature"/> is a valid MAC over <paramref name="body"/>; its timestamp
    /// and nonce when it is. Freshness and replay are the caller's to judge.
    /// </summary>
    public static bool TryVerify(
        byte[] key,
        ReadOnlySpan<byte> body,
        string? signature,
        out long timestamp,
        out string nonce
    )
    {
        EnsureKey(key, nameof(key));
        if (!TryParse(signature, out timestamp, out nonce, out var presented))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            RequestMac(key, body, timestamp, nonce),
            presented
        );
    }

    /// <summary>
    /// The signature for an answer to the request that carried <paramref name="requestSignature"/>,
    /// or null when that does not parse.
    /// </summary>
    public static string? SignResponse(
        byte[] key,
        string? requestSignature,
        ReadOnlySpan<byte> body
    )
    {
        EnsureKey(key, nameof(key));
        if (!TryParse(requestSignature, out _, out var nonce, out _))
            return null;
        return $"{Version},s={Convert.ToBase64String(ResponseMac(key, nonce, body))}";
    }

    /// <summary>Whether <paramref name="responseSignature"/> signs <paramref name="body"/> as the answer to <paramref name="requestSignature"/>.</summary>
    public static bool VerifyResponse(
        byte[] key,
        string requestSignature,
        string? responseSignature,
        ReadOnlySpan<byte> body
    )
    {
        EnsureKey(key, nameof(key));
        if (!TryParse(requestSignature, out _, out var nonce, out _))
            return false;
        if (
            string.IsNullOrEmpty(responseSignature)
            || responseSignature.Length > 128
            || !responseSignature.StartsWith($"{Version},s=", StringComparison.Ordinal)
        )
            return false;

        byte[] presented;
        try
        {
            presented = Convert.FromBase64String(responseSignature[(Version.Length + 3)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(ResponseMac(key, nonce, body), presented);
    }

    /// <summary>The timestamp of a well-formed signature, read before the body is, so a stale request is refused unread.</summary>
    public static bool TryReadTimestamp(string? signature, out long timestamp) =>
        TryParse(signature, out timestamp, out _, out _);

    private static bool TryParse(
        string? signature,
        out long timestamp,
        out string nonce,
        out byte[] mac
    )
    {
        timestamp = 0;
        nonce = "";
        mac = [];

        if (string.IsNullOrEmpty(signature) || signature.Length > 256)
            return false;

        var parts = signature.Split(',');
        if (
            parts.Length != 4
            || !string.Equals(parts[0], Version, StringComparison.Ordinal)
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

        nonce = parts[2][2..];
        if (nonce.Length is < 16 or > 64 || !nonce.All(char.IsAsciiHexDigit))
            return false;

        try
        {
            mac = Convert.FromBase64String(parts[3][2..]);
        }
        catch (FormatException)
        {
            return false;
        }

        return mac.Length == HMACSHA256.HashSizeInBytes;
    }

    private static byte[] RequestMac(
        byte[] key,
        ReadOnlySpan<byte> body,
        long timestamp,
        string nonce
    ) =>
        Mac(
            key,
            string.Create(
                CultureInfo.InvariantCulture,
                $"trax-sandbox-{Version}\nrequest\n{timestamp}\n{nonce}\n"
            ),
            body
        );

    private static byte[] ResponseMac(byte[] key, string nonce, ReadOnlySpan<byte> body) =>
        Mac(key, $"trax-sandbox-{Version}\nresponse\n{nonce}\n", body);

    private static byte[] Mac(byte[] key, string prefix, ReadOnlySpan<byte> body)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData(Encoding.UTF8.GetBytes(prefix));
        hmac.AppendData(body);
        return hmac.GetHashAndReset();
    }
}
