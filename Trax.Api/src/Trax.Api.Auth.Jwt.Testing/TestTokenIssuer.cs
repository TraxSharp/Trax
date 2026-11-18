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
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-auth/test-token-issuer">SDK reference</seealso>
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
    /// <see cref="TestJwksServer.CreateIssuer(string)"/> to enable kid rotation in
    /// tests.
    /// </summary>
    public TestTokenIssuer(
        string issuer,
        string defaultAudience,
        SigningCredentials credentials,
        IReadOnlyDictionary<string, SigningCredentials>? keyset
    )
        : this(issuer, defaultAudience, credentials, keyset, TimeProvider.System) { }

    /// <summary>
    /// Construct an issuer as the constructor without <paramref name="clock"/> does, whose
    /// tokens' default <c>nbf</c> and <c>exp</c> are read from <paramref name="clock"/>.
    /// </summary>
    public TestTokenIssuer(
        string issuer,
        string defaultAudience,
        SigningCredentials credentials,
        IReadOnlyDictionary<string, SigningCredentials>? keyset,
        TimeProvider clock
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultAudience);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(clock);
        Issuer = issuer;
        DefaultAudience = defaultAudience;
        _credentials = credentials;
        _keyset = keyset;
        Clock = clock;
    }

    /// <summary>The clock a token's default <c>nbf</c> and <c>exp</c> are read from.</summary>
    public TimeProvider Clock { get; }

    /// <summary>
    /// Return a new issuer that signs with the key identified by
    /// <paramref name="kid"/>. Throws if no key with that kid is in the
    /// issuer's keyset (only issuers minted via
    /// <see cref="TestJwksServer.CreateIssuer(string)"/> have a populated keyset).
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
        return new TestTokenIssuer(Issuer, DefaultAudience, creds, _keyset, Clock);
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
        var builder = new TestTokenBuilder(Issuer, DefaultAudience, Clock);
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
