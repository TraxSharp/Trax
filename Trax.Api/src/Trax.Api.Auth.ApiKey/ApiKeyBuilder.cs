namespace Trax.Api.Auth.ApiKey;

/// <summary>
/// Registration surface for static API-key sets. Keys configured through this
/// builder are stored as salted SHA-256 hashes and compared in constant time;
/// the raw comparison path is not reachable from consumer code.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class ApiKeyBuilder
{
    private readonly List<HashedApiKeyResolver.Entry> _entries = [];

    /// <summary>
    /// Registers a cleartext key mapped to a principal. The key is salted and
    /// hashed immediately; the cleartext is not retained after registration.
    /// </summary>
    /// <param name="key">The API key value callers present in the configured header.</param>
    /// <param name="id">Stable principal identifier (lands in the <see cref="TraxAuthClaimTypes.PrincipalId"/> claim).</param>
    /// <param name="roles">Roles to attach to the resolved principal.</param>
    public ApiKeyBuilder Add(string key, string id, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(roles);

        var rolesSnapshot = (string[])roles.Clone();
        return Add(key, () => new TraxPrincipal(id, id, rolesSnapshot, PrincipalType: "apikey"));
    }

    /// <summary>
    /// Registers a cleartext key with a custom principal factory. Use this when
    /// the principal needs a display name distinct from its id, or custom claims
    /// beyond roles.
    /// </summary>
    /// <param name="key">The API key value callers present in the configured header.</param>
    /// <param name="principalFactory">Invoked when the key matches; result becomes the authenticated principal.</param>
    public ApiKeyBuilder Add(string key, Func<TraxPrincipal> principalFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(principalFactory);

        _entries.Add(HashedApiKeyResolver.Entry.FromPlainKey(key, principalFactory));
        return this;
    }

    /// <summary>
    /// Registers a pre-hashed key. Cleartext never enters the process. Use this
    /// for production hosts that load salt and hash bytes from a secret manager.
    /// </summary>
    /// <param name="salt">Per-key salt bytes used when the hash was computed.</param>
    /// <param name="sha256">SHA-256 of <c>salt || utf8(cleartext)</c>.</param>
    /// <param name="id">Stable principal identifier.</param>
    /// <param name="roles">Roles to attach to the resolved principal.</param>
    public ApiKeyBuilder AddHashed(byte[] salt, byte[] sha256, string id, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(roles);

        var rolesSnapshot = (string[])roles.Clone();
        return AddHashed(
            salt,
            sha256,
            () => new TraxPrincipal(id, id, rolesSnapshot, PrincipalType: "apikey")
        );
    }

    /// <summary>
    /// Registers a pre-hashed key with a custom principal factory.
    /// </summary>
    /// <param name="salt">Per-key salt bytes used when the hash was computed.</param>
    /// <param name="sha256">SHA-256 of <c>salt || utf8(cleartext)</c>.</param>
    /// <param name="principalFactory">Invoked when the hash matches; result becomes the authenticated principal.</param>
    public ApiKeyBuilder AddHashed(byte[] salt, byte[] sha256, Func<TraxPrincipal> principalFactory)
    {
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(sha256);
        ArgumentNullException.ThrowIfNull(principalFactory);
        if (salt.Length == 0)
            throw new ArgumentException("Salt must be non-empty.", nameof(salt));
        if (sha256.Length != 32)
            throw new ArgumentException("SHA-256 hash must be exactly 32 bytes.", nameof(sha256));

        _entries.Add(new HashedApiKeyResolver.Entry(salt, sha256, principalFactory));
        return this;
    }

    internal HashedApiKeyResolver Build()
    {
        if (_entries.Count == 0)
            throw new InvalidOperationException(
                "AddTraxApiKeyAuth(keys => ...) requires at least one key. "
                    + "Call keys.Add(key, id, roles) or keys.AddHashed(salt, sha256, id, roles)."
            );
        return new HashedApiKeyResolver(_entries);
    }
}
