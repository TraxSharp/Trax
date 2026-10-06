namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Fluent configuration for <c>AddTraxJwtDispatcher</c>. Maps issuer URLs
/// (the <c>iss</c> claim on inbound tokens) to JWT scheme names registered
/// via <c>AddTraxJwtAuth</c>.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Issuer comparison is ordinal, case-sensitive, and trailing-slash sensitive.
/// Configure each issuer string to match exactly what the IdP emits in the
/// <c>iss</c> claim. The dispatcher reads <c>iss</c> from the token without
/// validating its signature; the matched scheme then performs full
/// validation (signature, issuer, audience, lifetime).
/// </para>
/// </remarks>
public sealed class JwtDispatcherBuilder
{
    private readonly Dictionary<string, string> _issuerToScheme = new(StringComparer.Ordinal);

    internal string SchemeName { get; private set; } = JwtDefaults.DispatcherSchemeName;
    internal string? FallbackSchemeName { get; private set; }
    internal IReadOnlyDictionary<string, string> Mappings => _issuerToScheme;

    /// <summary>
    /// Maps an <c>iss</c> value to the JWT scheme that should validate it.
    /// Duplicate issuer registrations throw at build time, since a Bearer
    /// token cannot be validated by two schemes at once.
    /// </summary>
    public JwtDispatcherBuilder MapIssuer(string issuer, string schemeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        if (_issuerToScheme.ContainsKey(issuer))
            throw new InvalidOperationException(
                $"Issuer '{issuer}' is already mapped to scheme '{_issuerToScheme[issuer]}'. "
                    + "Each issuer can route to only one scheme."
            );
        _issuerToScheme[issuer] = schemeName;
        return this;
    }

    /// <summary>
    /// Overrides the dispatcher's own scheme name. Defaults to
    /// <see cref="JwtDefaults.DispatcherSchemeName"/>. Set when the host
    /// already uses a scheme named <c>TraxJwtDispatcher</c> for an unrelated
    /// handler.
    /// </summary>
    public JwtDispatcherBuilder WithSchemeName(string schemeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        SchemeName = schemeName;
        return this;
    }

    /// <summary>
    /// Scheme to dispatch to when a token's <c>iss</c> claim matches none of
    /// the configured mappings. Defaults to the rejection scheme, which
    /// produces a 401. Set to one of the registered JWT scheme names to
    /// admit tokens with unmapped issuers (the chosen scheme will perform
    /// its own issuer validation).
    /// </summary>
    public JwtDispatcherBuilder FallbackToScheme(string schemeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        FallbackSchemeName = schemeName;
        return this;
    }

    internal void Validate()
    {
        if (_issuerToScheme.Count == 0)
            throw new InvalidOperationException(
                "AddTraxJwtDispatcher(d => ...) requires at least one MapIssuer(issuer, schemeName) call."
            );
    }
}
