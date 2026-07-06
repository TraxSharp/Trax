namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Runtime view of an <c>AddTraxJwtDispatcher</c> registration: the issuer to
/// scheme routing table plus per-scheme principal-resolver resolution. The HTTP
/// path routes tokens by <c>iss</c> through a policy scheme; this exposes the
/// same routing to the GraphQL subscription socket interceptor, which has no
/// authentication middleware to lean on.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Registered as a singleton by <c>AddTraxJwtDispatcher</c>. The token's issuer
/// is read without validating its signature and is used only to select a scheme;
/// that scheme then performs full validation.
/// </para>
/// </remarks>
public sealed class JwtDispatcherRuntime
{
    private readonly IReadOnlyDictionary<string, string> _issuerToScheme;
    private readonly string? _fallbackScheme;
    private readonly JwtResolverRegistry _registry;

    internal JwtDispatcherRuntime(
        IReadOnlyDictionary<string, string> issuerToScheme,
        string? fallbackScheme,
        JwtResolverRegistry registry
    )
    {
        _issuerToScheme = issuerToScheme;
        _fallbackScheme = fallbackScheme;
        _registry = registry;
    }

    /// <summary>The configured issuer to scheme-name routing table.</summary>
    public IReadOnlyDictionary<string, string> IssuerToScheme => _issuerToScheme;

    /// <summary>
    /// Selects the JWT scheme that should validate <paramref name="token"/> based
    /// on its (unvalidated) <c>iss</c> claim. Returns the configured fallback
    /// scheme for unmapped issuers, or <c>null</c> when there is no fallback and
    /// the connection must be rejected.
    /// </summary>
    public string? ResolveSchemeForToken(string token)
    {
        var issuer = JwtIssuerPeek.TryReadIssuer(token);
        if (issuer is not null && _issuerToScheme.TryGetValue(issuer, out var scheme))
            return scheme;
        return _fallbackScheme;
    }

    /// <summary>
    /// Resolves the <see cref="ITraxPrincipalResolver{JwtTokenInput}"/> registered
    /// for <paramref name="schemeName"/>, or <c>null</c> when the scheme has no
    /// resolver. Pass a scoped <paramref name="services"/> so scoped resolvers
    /// resolve correctly.
    /// </summary>
    public ITraxPrincipalResolver<JwtTokenInput>? ResolvePrincipalResolver(
        string schemeName,
        IServiceProvider services
    ) => _registry.TryResolve(schemeName, services);
}
