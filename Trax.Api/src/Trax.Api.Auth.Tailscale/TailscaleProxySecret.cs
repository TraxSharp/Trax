using System.Security.Cryptography;
using System.Text;

namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// The proxy's secret, held as its SHA-256 digest. A presented value is hashed and the two digests compared with
/// <see cref="CryptographicOperations.FixedTimeEquals"/>, so neither the time the comparison takes nor where it stops
/// says how much of a guess was right, and both sides are 32 bytes whatever the guess's length.
/// </summary>
internal sealed class TailscaleProxySecret
{
    private readonly byte[] _digest;

    public TailscaleProxySecret(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        _digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>Whether <paramref name="presented"/> is the secret.</summary>
    public bool Matches(string presented)
    {
        ArgumentNullException.ThrowIfNull(presented);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(digest, _digest);
    }
}
