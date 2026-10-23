using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Trax.Scheduler.Services.RequestSigning;

/// <summary>
/// What a signed runner request is for. Part of the signed text, so a signature made for one
/// purpose does not verify for the other.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-job-runner#infrastructure-types">SDK reference</seealso>
public enum RunnerRequestPurpose
{
    /// <summary>A queued job: <c>/trax/execute</c>, a Lambda <c>Execute</c> envelope, or an SQS message.</summary>
    Execute,

    /// <summary>A synchronous run: <c>/trax/run</c> or a Lambda <c>Run</c> envelope.</summary>
    Run,
}

/// <summary>
/// The signature a scheduler puts on a request to a runner, and the check a runner makes on it.
/// </summary>
/// <remarks>
/// <para>
/// The value is <c>v1,t=&lt;unix seconds&gt;,n=&lt;nonce&gt;,s=&lt;base64 HMAC-SHA256&gt;</c>, carried in
/// the <see cref="HeaderName"/> HTTP header, the <c>Signature</c> of a Lambda envelope, or an SQS
/// message attribute of the same name. The MAC covers the version, the purpose, the timestamp, the
/// nonce and the exact body bytes, keyed with a secret both processes share (see scheduler/0006).
/// </para>
/// <para>
/// Public because it is the contract between packages that ship separately: Trax.Scheduler signs
/// and verifies, Trax.Scheduler.Lambda and Trax.Scheduler.Sqs sign, and Trax.Runner.Lambda verifies.
/// </para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-job-runner#infrastructure-types">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RunnerRequestSignature
{
    /// <summary>The HTTP header and SQS message attribute that carry the signature.</summary>
    public const string HeaderName = "Trax-Signature";

    /// <summary>The shortest key accepted, in bytes: the HMAC-SHA256 block of entropy.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>
    /// The HTTP response header a runner with a signing key puts on a run's answer: a MAC over the
    /// purpose, the nonce of the request it answers and the exact body, so the scheduler accepts
    /// only an answer the runner made for that request.
    /// </summary>
    public const string ResponseHeaderName = "Trax-Response-Signature";

    private const string Version = "v1";

    /// <summary>
    /// Signs <paramref name="body"/> for <paramref name="purpose"/> with a fresh timestamp and nonce.
    /// </summary>
    /// <param name="key">The shared key, at least <see cref="MinimumKeyLength"/> bytes.</param>
    /// <param name="purpose">What the request is for.</param>
    /// <param name="body">The exact bytes that will be sent.</param>
    /// <returns>The value for the <see cref="HeaderName"/> header or attribute.</returns>
    public static string Create(byte[] key, RunnerRequestPurpose purpose, ReadOnlySpan<byte> body)
    {
        EnsureKey(key, nameof(key));
        return Create(
            key,
            purpose,
            body,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
        );
    }

    internal static string Create(
        byte[] key,
        RunnerRequestPurpose purpose,
        ReadOnlySpan<byte> body,
        long timestamp,
        string nonce
    )
    {
        var mac = ComputeMac(key, purpose, body, timestamp, nonce);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Version},t={timestamp},n={nonce},s={Convert.ToBase64String(mac)}"
        );
    }

    /// <summary>
    /// Throws when <paramref name="key"/> is missing or shorter than <see cref="MinimumKeyLength"/>.
    /// </summary>
    public static void EnsureKey(byte[]? key, string paramName)
    {
        if (key is null || key.Length < MinimumKeyLength)
            throw new ArgumentException(
                $"A runner signing key must be at least {MinimumKeyLength} bytes "
                    + "(for example, 32 random bytes, base64-encoded in configuration).",
                paramName
            );
    }

    /// <summary>
    /// Checks the MAC of <paramref name="signature"/> against <paramref name="body"/>, and returns its
    /// timestamp and nonce when it matches. Freshness and replay are the caller's to judge.
    /// </summary>
    internal static bool TryVerifyMac(
        byte[] key,
        RunnerRequestPurpose purpose,
        ReadOnlySpan<byte> body,
        string? signature,
        out long timestamp,
        out string nonce
    )
    {
        timestamp = 0;
        nonce = "";

        if (!TryParse(signature, out timestamp, out nonce, out var presented))
            return false;

        var expected = ComputeMac(key, purpose, body, timestamp, nonce);
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

    /// <summary>
    /// Signs a runner's answer to the request that carried <paramref name="requestSignature"/>:
    /// <c>v1,s=&lt;base64 HMAC-SHA256&gt;</c> over the purpose, that request's nonce and the
    /// exact <paramref name="body"/>. Null when the request signature does not parse, which a
    /// request the runner verified always does.
    /// </summary>
    internal static string? CreateResponse(
        byte[] key,
        RunnerRequestPurpose purpose,
        string? requestSignature,
        ReadOnlySpan<byte> body
    )
    {
        if (!TryParse(requestSignature, out _, out var nonce, out _))
            return null;

        var mac = ComputeResponseMac(key, purpose, nonce, body);
        return $"{Version},s={Convert.ToBase64String(mac)}";
    }

    /// <summary>
    /// Whether <paramref name="responseSignature"/> is the runner's signature over
    /// <paramref name="body"/> as the answer to the request that carried
    /// <paramref name="requestSignature"/>. A missing, malformed or mismatched one is false, as is
    /// a signature made for another request.
    /// </summary>
    internal static bool VerifyResponse(
        byte[] key,
        RunnerRequestPurpose purpose,
        string requestSignature,
        string? responseSignature,
        ReadOnlySpan<byte> body
    )
    {
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

        var expected = ComputeResponseMac(key, purpose, nonce, body);
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

    /// <summary>
    /// Whether <paramref name="signature"/> is well formed, and its timestamp when it is. Checks
    /// nothing a body is needed for, so a request can be refused before its body is read.
    /// </summary>
    internal static bool TryReadTimestamp(string? signature, out long timestamp) =>
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
        if (parts.Length != 4 || parts[0] != Version)
            return false;

        if (
            !parts[1].StartsWith("t=", StringComparison.Ordinal)
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

    private static byte[] ComputeMac(
        byte[] key,
        RunnerRequestPurpose purpose,
        ReadOnlySpan<byte> body,
        long timestamp,
        string nonce
    )
    {
        var prefix = Encoding.UTF8.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"trax-runner-{Version}\n{PurposeName(purpose)}\n{timestamp}\n{nonce}\n"
            )
        );

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData(prefix);
        hmac.AppendData(body);
        return hmac.GetHashAndReset();
    }

    // A prefix of its own, so a response MAC can never be presented as a request MAC or the
    // other way round.
    private static byte[] ComputeResponseMac(
        byte[] key,
        RunnerRequestPurpose purpose,
        string requestNonce,
        ReadOnlySpan<byte> body
    )
    {
        var prefix = Encoding.UTF8.GetBytes(
            $"trax-runner-response-{Version}\n{PurposeName(purpose)}\n{requestNonce}\n"
        );

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData(prefix);
        hmac.AppendData(body);
        return hmac.GetHashAndReset();
    }

    private static string PurposeName(RunnerRequestPurpose purpose) =>
        purpose switch
        {
            RunnerRequestPurpose.Execute => "execute",
            RunnerRequestPurpose.Run => "run",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };
}
