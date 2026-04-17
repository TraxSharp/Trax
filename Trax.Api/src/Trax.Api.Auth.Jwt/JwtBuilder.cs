using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Fluent configuration for the Trax JWT bearer scheme. Values set here are
/// applied to the underlying <see cref="JwtBearerOptions"/> at startup.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Pick exactly one key source: either an OIDC authority (JWKS discovery) via
/// <see cref="UseAuthority"/>, or an explicit signing key via <see cref="UseSigningKey"/>
/// or <see cref="UseSymmetricKey(string, string, byte[])"/>. Registering neither
/// or both throws at build time.
/// </para>
/// </remarks>
public sealed class JwtBuilder
{
    internal string? Authority { get; private set; }
    internal string? Audience { get; private set; }
    internal string? Issuer { get; private set; }
    internal SecurityKey? SigningKey { get; private set; }
    internal TimeSpan? ClockSkew { get; private set; }
    internal bool RequireHttpsMetadata { get; private set; } = true;
    internal Action<JwtBearerOptions>? BearerOptionsCustomizer { get; private set; }
    internal Action<TokenValidationParameters>? TokenValidationCustomizer { get; private set; }

    /// <summary>
    /// Configures the scheme to fetch signing keys from an OIDC-compliant
    /// authority's JWKS endpoint. The underlying handler refreshes the
    /// document and caches keys for you.
    /// </summary>
    /// <param name="authority">Issuer URL (e.g. <c>https://login.example.com</c>). Must be HTTPS unless <see cref="AllowHttpMetadata"/> is called.</param>
    /// <param name="audience">Expected <c>aud</c> claim value.</param>
    public JwtBuilder UseAuthority(string authority, string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        Authority = authority;
        Audience = audience;
        return this;
    }

    /// <summary>
    /// Configures a fixed HMAC-SHA256 signing key. Suitable for internal
    /// services that mint their own tokens.
    /// </summary>
    /// <param name="issuer">Expected <c>iss</c> claim value.</param>
    /// <param name="audience">Expected <c>aud</c> claim value.</param>
    /// <param name="key">Key material; must be at least 32 bytes for HS256.</param>
    public JwtBuilder UseSymmetricKey(string issuer, string audience, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
            throw new ArgumentException(
                "Symmetric signing key must be at least 32 bytes (256 bits) for HS256.",
                nameof(key)
            );

        return UseSigningKey(issuer, audience, new SymmetricSecurityKey(key));
    }

    /// <summary>
    /// Configures an arbitrary <see cref="SecurityKey"/> (asymmetric or symmetric).
    /// Use when keys are loaded from a secret manager or certificate store.
    /// </summary>
    public JwtBuilder UseSigningKey(string issuer, string audience, SecurityKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(key);
        Issuer = issuer;
        Audience = audience;
        SigningKey = key;
        return this;
    }

    /// <summary>
    /// Overrides the default clock skew (<c>TokenValidationParameters</c>
    /// defaults to 5 minutes). Set to <see cref="TimeSpan.Zero"/> for strict
    /// validation against synchronized clocks.
    /// </summary>
    public JwtBuilder WithClockSkew(TimeSpan skew)
    {
        if (skew < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(skew), "Clock skew cannot be negative.");
        ClockSkew = skew;
        return this;
    }

    /// <summary>
    /// Permits non-HTTPS authority metadata. Development and test only; never
    /// enable in production, since MITM on metadata defeats token validation.
    /// </summary>
    public JwtBuilder AllowHttpMetadata()
    {
        RequireHttpsMetadata = false;
        return this;
    }

    /// <summary>
    /// Hook for additional <see cref="TokenValidationParameters"/> tweaks
    /// (custom lifetime validators, audience lists, type validation, etc.).
    /// Called after Trax sets issuer, audience, signing key, and clock skew,
    /// so this is the last writer.
    /// </summary>
    public JwtBuilder CustomizeTokenValidation(Action<TokenValidationParameters> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        TokenValidationCustomizer = configure;
        return this;
    }

    /// <summary>
    /// Hook for raw <see cref="JwtBearerOptions"/> access (event handlers for
    /// <c>OnChallenge</c>, <c>OnAuthenticationFailed</c>, etc.). Runs after
    /// Trax has wired <c>OnTokenValidated</c> to the principal resolver, so
    /// do not overwrite the events collection wholesale.
    /// </summary>
    public JwtBuilder CustomizeBearerOptions(Action<JwtBearerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        BearerOptionsCustomizer = configure;
        return this;
    }

    internal void Validate()
    {
        var hasAuthority = Authority is not null;
        var hasExplicitKey = SigningKey is not null;

        if (!hasAuthority && !hasExplicitKey)
            throw new InvalidOperationException(
                "AddTraxJwtAuth(jwt => ...) requires either UseAuthority(authority, audience) "
                    + "for OIDC discovery, or UseSigningKey(issuer, audience, key) / "
                    + "UseSymmetricKey(issuer, audience, keyBytes) for an explicit signing key."
            );

        if (hasAuthority && hasExplicitKey)
            throw new InvalidOperationException(
                "AddTraxJwtAuth(jwt => ...) cannot mix UseAuthority with UseSigningKey. "
                    + "Pick one key source."
            );
    }
}
