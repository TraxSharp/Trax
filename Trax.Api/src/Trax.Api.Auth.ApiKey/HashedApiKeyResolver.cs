using System.Security.Cryptography;
using System.Text;

namespace Trax.Api.Auth.ApiKey;

/// <summary>
/// Constant-time API key resolver. Keys are held as salted SHA-256 hashes and
/// compared with <see cref="CryptographicOperations.FixedTimeEquals"/>. The
/// resolver iterates every configured entry regardless of early matches so the
/// wall-clock cost of a lookup is independent of which (if any) entry matches.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. See SECURITY-DISCLAIMER.md.
/// <para>
/// Built by <see cref="ApiKeyBuilder"/> from keys registered via
/// <c>AddTraxApiKeyAuth(keys =&gt; ...)</c>. Entries are immutable once registered;
/// key rotation requires rebuilding the resolver with a new entry set. The
/// resolver is safe to register as a singleton.
/// </para>
/// </remarks>
public sealed class HashedApiKeyResolver : ITraxPrincipalResolver<string>
{
    /// <summary>
    /// One record per valid API key. <paramref name="Salt"/> and
    /// <paramref name="Sha256Hash"/> are both raw bytes (not Base64). The
    /// <paramref name="PrincipalFactory"/> is invoked only when the hash match
    /// succeeds, so per-principal state is not materialized for failed lookups.
    /// </summary>
    public sealed record Entry(byte[] Salt, byte[] Sha256Hash, Func<TraxPrincipal> PrincipalFactory)
    {
        /// <summary>
        /// Builds an <see cref="Entry"/> from a cleartext key. Useful for tests and
        /// bootstrap scripts. Production hosts should ship with pre-hashed entries
        /// so the cleartext never enters process memory outside of lookup.
        /// </summary>
        public static Entry FromPlainKey(
            string cleartextKey,
            Func<TraxPrincipal> principalFactory,
            int saltBytes = 16
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cleartextKey);
            ArgumentNullException.ThrowIfNull(principalFactory);
            if (saltBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(saltBytes));

            var salt = RandomNumberGenerator.GetBytes(saltBytes);
            var hash = Hash(salt, cleartextKey);
            return new Entry(salt, hash, principalFactory);
        }
    }

    private readonly IReadOnlyList<Entry> _entries;

    /// <summary>
    /// Builds a resolver over the given entry set. The entries are snapshotted; later
    /// mutations to the source enumerable are not observed.
    /// </summary>
    public HashedApiKeyResolver(IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input))
            return new ValueTask<TraxPrincipal?>((TraxPrincipal?)null);

        // Hash once, compare against every entry without short-circuiting. Bitwise
        // OR of the per-entry results collapses "any match" into a single boolean
        // without branching on intermediate results.
        Entry? matched = null;
        var anyMatch = 0;
        foreach (var entry in _entries)
        {
            var candidate = Hash(entry.Salt, input);
            var isMatch = CryptographicOperations.FixedTimeEquals(candidate, entry.Sha256Hash)
                ? 1
                : 0;
            anyMatch |= isMatch;
            if (isMatch == 1 && matched is null)
                matched = entry;
        }

        if (anyMatch == 0 || matched is null)
            return new ValueTask<TraxPrincipal?>((TraxPrincipal?)null);

        return new ValueTask<TraxPrincipal?>(matched.PrincipalFactory());
    }

    internal static byte[] Hash(byte[] salt, string cleartext)
    {
        var cleartextBytes = Encoding.UTF8.GetBytes(cleartext);
        var buffer = new byte[salt.Length + cleartextBytes.Length];
        Buffer.BlockCopy(salt, 0, buffer, 0, salt.Length);
        Buffer.BlockCopy(cleartextBytes, 0, buffer, salt.Length, cleartextBytes.Length);
        return SHA256.HashData(buffer);
    }
}
