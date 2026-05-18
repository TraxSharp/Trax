using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.Auth.Jwt.Testing;

/// <summary>
/// Mints signed JWTs for use in Trax integration tests. Construct via
/// <see cref="Symmetric"/> for HS256 tokens or pair with a
/// <see cref="TestJwksServer"/> for RS256 tokens.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class TestTokenIssuer
{
    private readonly SigningCredentials _credentials;
    private readonly IReadOnlyDictionary<string, SigningCredentials>? _keyset;

    /// <summary>Issuer URL placed in the <c>iss</c> claim by default.</summary>
    public string Issuer { get; }

    /// <summary>Default audience placed in <c>aud</c> when not overridden per call.</summary>
    public string DefaultAudience { get; }

    /// <summary>Current signing credentials. Switch with <see cref="WithSigningKey"/>.</summary>
    public SigningCredentials Credentials => _credentials;

    /// <summary>Construct an issuer that signs with the given credentials.</summary>
    public TestTokenIssuer(string issuer, string defaultAudience, SigningCredentials credentials)
        : this(issuer, defaultAudience, credentials, keyset: null) { }

    /// <summary>
    /// Construct an issuer that signs with <paramref name="credentials"/> by
    /// default but can switch to any other entry in <paramref name="keyset"/>
    /// via <see cref="WithSigningKey"/>. Used by
    /// <see cref="TestJwksServer.CreateIssuer"/> to enable kid rotation in
    /// tests.
    /// </summary>
    public TestTokenIssuer(
        string issuer,
        string defaultAudience,
        SigningCredentials credentials,
        IReadOnlyDictionary<string, SigningCredentials>? keyset
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultAudience);
        ArgumentNullException.ThrowIfNull(credentials);
        Issuer = issuer;
        DefaultAudience = defaultAudience;
        _credentials = credentials;
        _keyset = keyset;
    }

    /// <summary>
    /// Return a new issuer that signs with the key identified by
    /// <paramref name="kid"/>. Throws if no key with that kid is in the
    /// issuer's keyset (only issuers minted via
    /// <see cref="TestJwksServer.CreateIssuer"/> have a populated keyset).
    /// </summary>
    public TestTokenIssuer WithSigningKey(string kid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        if (_keyset is null)
            throw new InvalidOperationException(
                "WithSigningKey requires an issuer constructed with a keyset. "
                    + "Use TestJwksServer.CreateIssuer(audience) to obtain one."
            );
        if (!_keyset.TryGetValue(kid, out var creds))
            throw new ArgumentException(
                $"No signing key with kid '{kid}' is registered on this issuer. "
                    + $"Known kids: [{string.Join(", ", _keyset.Keys)}]",
                nameof(kid)
            );
        return new TestTokenIssuer(Issuer, DefaultAudience, creds, _keyset);
    }

    /// <summary>
    /// Construct an HS256 issuer. <paramref name="key"/> must be at least
    /// 32 bytes (HS256 minimum).
    /// </summary>
    public static TestTokenIssuer Symmetric(string issuer, string audience, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
            throw new ArgumentException(
                "HS256 signing key must be at least 32 bytes (256 bits).",
                nameof(key)
            );

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256
        );
        return new TestTokenIssuer(issuer, audience, creds);
    }

    /// <summary>
    /// Mint a token. The configure callback receives a builder pre-seeded
    /// with this issuer's defaults; callers add claims, override the
    /// audience, or set a custom lifetime.
    /// </summary>
    public string Mint(Action<TestTokenBuilder>? configure = null)
    {
        var builder = new TestTokenBuilder(Issuer, DefaultAudience);
        configure?.Invoke(builder);

        var token = new JwtSecurityToken(
            issuer: builder.IssuerValue,
            audience: builder.AudienceValue,
            claims: builder.GetClaims(),
            notBefore: builder.NotBeforeValue,
            expires: builder.ExpiresValue,
            signingCredentials: _credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Fluent builder for an individual JWT minted by <see cref="TestTokenIssuer"/>.
/// </summary>
public sealed class TestTokenBuilder
{
    private readonly List<Claim> _claims = new();

    internal TestTokenBuilder(string issuer, string audience)
    {
        IssuerValue = issuer;
        AudienceValue = audience;
        NotBeforeValue = DateTime.UtcNow.AddMinutes(-1);
        ExpiresValue = DateTime.UtcNow.AddMinutes(5);
    }

    internal string IssuerValue { get; private set; }
    internal string AudienceValue { get; private set; }
    internal DateTime NotBeforeValue { get; private set; }
    internal DateTime ExpiresValue { get; private set; }

    /// <summary>Sets the <c>sub</c> claim.</summary>
    public TestTokenBuilder WithSubject(string sub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        _claims.RemoveAll(c => c.Type == "sub");
        _claims.Add(new Claim("sub", sub));
        return this;
    }

    /// <summary>Appends a claim. Multiple calls with the same type are allowed.</summary>
    public TestTokenBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new Claim(type, value));
        return this;
    }

    /// <summary>Appends a role claim (<see cref="ClaimTypes.Role"/>).</summary>
    public TestTokenBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _claims.Add(new Claim(ClaimTypes.Role, role));
        return this;
    }

    /// <summary>Overrides the issuer (<c>iss</c>) for this token only.</summary>
    public TestTokenBuilder WithIssuer(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        IssuerValue = issuer;
        return this;
    }

    /// <summary>Overrides the audience (<c>aud</c>) for this token only.</summary>
    public TestTokenBuilder WithAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        AudienceValue = audience;
        return this;
    }

    /// <summary>Sets the not-before timestamp.</summary>
    public TestTokenBuilder WithNotBefore(DateTime notBefore)
    {
        NotBeforeValue = notBefore;
        return this;
    }

    /// <summary>Sets the expiration timestamp.</summary>
    public TestTokenBuilder WithExpires(DateTime expires)
    {
        ExpiresValue = expires;
        return this;
    }

    /// <summary>Sets the lifetime relative to now (nbf = now-1min, exp = now+lifetime).</summary>
    public TestTokenBuilder WithLifetime(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Lifetime must be positive.");
        NotBeforeValue = DateTime.UtcNow.AddMinutes(-1);
        ExpiresValue = DateTime.UtcNow.Add(lifetime);
        return this;
    }

    internal IEnumerable<Claim> GetClaims() => _claims;
}
