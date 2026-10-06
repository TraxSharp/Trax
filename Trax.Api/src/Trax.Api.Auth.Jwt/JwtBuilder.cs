using System.Text;
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
    /// <summary>
    /// The marker the Trax templates and samples put on their demo signing keys. A symmetric key
    /// whose bytes, read as UTF-8, contain it starts only in Development.
    /// </summary>
    internal const string DemoKeyMarker = "do-not-use-in-production";

    internal string? Authority { get; private set; }
    internal string? Audience { get; private set; }
    internal string? Issuer { get; private set; }
    internal SecurityKey? SigningKey { get; private set; }
    internal TimeSpan? ClockSkew { get; private set; }
    internal bool RequireHttpsMetadata { get; private set; } = true;
    internal Action<JwtBearerOptions>? BearerOptionsCustomizer { get; private set; }
    internal Action<TokenValidationParameters>? TokenValidationCustomizer { get; private set; }

    /// <summary>
    /// True when <paramref name="key"/> is symmetric key material carrying
    /// <see cref="DemoKeyMarker"/>: a <see cref="SymmetricSecurityKey"/>, or a
    /// <see cref="JsonWebKey"/> of type <c>oct</c>.
    /// </summary>
    internal static bool IsDemoKey(SecurityKey? key)
    {
        var bytes = key switch
        {
            SymmetricSecurityKey symmetric => symmetric.Key,
            JsonWebKey { Kty: JsonWebAlgorithmsKeyTypes.Octet, K: { Length: > 0 } k } =>
                DecodeOrNull(k),
            _ => null,
        };
        return bytes is not null
            && Encoding
                .UTF8.GetString(bytes)
                .Contains(DemoKeyMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? DecodeOrNull(string base64Url)
    {
        try
        {
            return Base64UrlEncoder.DecodeBytes(base64Url);
        }
        catch (FormatException)
        {
            // Not key material the handler could use either; the handler reports it.
            return null;
        }
    }

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
    /// <param name="key">
    /// Key material; must be at least 32 bytes for HS256. A key containing
    /// <c>do-not-use-in-production</c> starts only in Development.
    /// </param>
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
    /// Use when keys are loaded from a secret manager or certificate store. A symmetric key
    /// containing <c>do-not-use-in-production</c> starts only in Development.
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
    /// Called after Trax sets issuer, audience, signing key, and clock skew.
    /// Multiple calls chain: each callback runs in registration order, so
    /// helpers (e.g. <c>UseCognito</c>) and consumer overrides can compose.
    /// A symmetric signing key set here that contains <c>do-not-use-in-production</c> starts
    /// only in Development, as one passed to <see cref="UseSigningKey"/> does.
    /// </summary>
    public JwtBuilder CustomizeTokenValidation(Action<TokenValidationParameters> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (TokenValidationCustomizer is null)
        {
            TokenValidationCustomizer = configure;
        }
        else
        {
            var previous = TokenValidationCustomizer;
            TokenValidationCustomizer = tvp =>
            {
                previous(tvp);
                configure(tvp);
            };
        }
        return this;
    }

    /// <summary>
    /// Hook for raw <see cref="JwtBearerOptions"/> access (event handlers for
    /// <c>OnChallenge</c>, <c>OnAuthenticationFailed</c>, etc.). Runs after
    /// Trax has wired <c>OnTokenValidated</c> to the principal resolver, so
    /// do not overwrite the events collection wholesale. Multiple calls
    /// chain in registration order. A symmetric signing key set here that contains
    /// <c>do-not-use-in-production</c> starts only in Development, as one passed to
    /// <see cref="UseSigningKey"/> does.
    /// </summary>
    public JwtBuilder CustomizeBearerOptions(Action<JwtBearerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (BearerOptionsCustomizer is null)
        {
            BearerOptionsCustomizer = configure;
        }
        else
        {
            var previous = BearerOptionsCustomizer;
            BearerOptionsCustomizer = options =>
            {
                previous(options);
                configure(options);
            };
        }
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
