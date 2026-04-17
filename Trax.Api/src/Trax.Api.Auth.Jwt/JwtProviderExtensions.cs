using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Convenience wrappers around <see cref="JwtAuthServiceCollectionExtensions.AddTraxJwtAuth(IServiceCollection, string, string)"/>
/// that bake in the authority URLs and (where relevant) audience semantics for
/// common identity providers. Each method is a one-liner for the common case
/// plus a <typeparamref name="TResolver"/> overload for hosts that need
/// claim-to-principal enrichment beyond the default resolver.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// These helpers exist purely to hide non-obvious authority-URL shapes. They
/// delegate to <c>AddTraxJwtAuth</c>, so everything that works with the base
/// method (custom resolvers, combined <c>TraxAuthPolicy</c>, subscription
/// interceptor, etc.) works here too.
/// </para>
/// </remarks>
public static class JwtProviderExtensions
{
    // ── Google ───────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the Trax JWT scheme against Google as the identity provider.
    /// Uses <c>https://accounts.google.com</c> as the authority and the
    /// supplied OAuth 2.0 client id as the expected <c>aud</c> claim.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientId">
    /// The OAuth 2.0 client id registered in Google Cloud Console. Google
    /// id-tokens carry this value in <c>aud</c>; mismatches are rejected.
    /// </param>
    public static AuthenticationBuilder AddTraxGoogleJwtAuth(
        this IServiceCollection services,
        string clientId
    ) => services.AddTraxJwtAuth("https://accounts.google.com", clientId);

    /// <summary>
    /// Registers the Trax JWT scheme against Google, resolving principals via
    /// <typeparamref name="TResolver"/>. Use when you need database enrichment
    /// or non-standard role mapping.
    /// </summary>
    public static AuthenticationBuilder AddTraxGoogleJwtAuth<TResolver>(
        this IServiceCollection services,
        string clientId
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput> =>
        services.AddTraxJwtAuth<TResolver>("https://accounts.google.com", clientId);

    // ── Auth0 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the Trax JWT scheme against an Auth0 tenant. Authority is
    /// <c>https://{domain}/</c> (trailing slash required by Auth0's discovery
    /// doc). Audience is the Auth0 API identifier, which is distinct from the
    /// application client id in Auth0.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="domain">
    /// Auth0 tenant domain, either <c>my-tenant.auth0.com</c> or a custom
    /// domain. Do not include the scheme or the trailing slash — this helper
    /// normalizes.
    /// </param>
    /// <param name="audience">The Auth0 API identifier registered for your API.</param>
    public static AuthenticationBuilder AddTraxAuth0JwtAuth(
        this IServiceCollection services,
        string domain,
        string audience
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return services.AddTraxJwtAuth(BuildAuth0Authority(domain), audience);
    }

    /// <summary>
    /// Registers the Trax JWT scheme against an Auth0 tenant, resolving
    /// principals via <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxAuth0JwtAuth<TResolver>(
        this IServiceCollection services,
        string domain,
        string audience
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return services.AddTraxJwtAuth<TResolver>(BuildAuth0Authority(domain), audience);
    }

    // ── Microsoft Entra ID (formerly Azure AD) ───────────────────────────

    /// <summary>
    /// Registers the Trax JWT scheme against a Microsoft Entra tenant using
    /// the v2.0 endpoint. Authority is
    /// <c>https://login.microsoftonline.com/{tenantId}/v2.0</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tenantId">
    /// Directory (tenant) GUID or verified domain (e.g. <c>contoso.onmicrosoft.com</c>).
    /// Pass <c>common</c> for multi-tenant apps that accept any work/school or
    /// personal Microsoft account; pass <c>organizations</c> for any Entra
    /// tenant. Those special values come with their own signing-key rules —
    /// prefer a specific tenantId unless you explicitly need multi-tenancy.
    /// </param>
    /// <param name="audience">
    /// The registered application's Application (client) ID, or the App ID
    /// URI it exposes for the API.
    /// </param>
    public static AuthenticationBuilder AddTraxEntraJwtAuth(
        this IServiceCollection services,
        string tenantId,
        string audience
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return services.AddTraxJwtAuth(BuildEntraAuthority(tenantId), audience);
    }

    /// <summary>
    /// Registers the Trax JWT scheme against Microsoft Entra, resolving
    /// principals via <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxEntraJwtAuth<TResolver>(
        this IServiceCollection services,
        string tenantId,
        string audience
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return services.AddTraxJwtAuth<TResolver>(BuildEntraAuthority(tenantId), audience);
    }

    // ── Amazon Cognito ───────────────────────────────────────────────────

    /// <summary>
    /// Registers the Trax JWT scheme against an Amazon Cognito user pool.
    /// Authority is <c>https://cognito-idp.{region}.amazonaws.com/{userPoolId}</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="region">AWS region of the user pool, e.g. <c>us-east-1</c>.</param>
    /// <param name="userPoolId">
    /// The full user pool ID. Conventionally includes the region as a prefix
    /// (e.g. <c>us-east-1_AbCdEfGhI</c>); this helper doesn't parse it.
    /// </param>
    /// <param name="audience">
    /// The Cognito app client id. Note that Cognito id-tokens set <c>aud</c>
    /// to the client id, while access tokens use <c>client_id</c> instead —
    /// this helper is configured for id-token validation.
    /// </param>
    public static AuthenticationBuilder AddTraxCognitoJwtAuth(
        this IServiceCollection services,
        string region,
        string userPoolId,
        string audience
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPoolId);
        return services.AddTraxJwtAuth(BuildCognitoAuthority(region, userPoolId), audience);
    }

    /// <summary>
    /// Registers the Trax JWT scheme against Amazon Cognito, resolving
    /// principals via <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxCognitoJwtAuth<TResolver>(
        this IServiceCollection services,
        string region,
        string userPoolId,
        string audience
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPoolId);
        return services.AddTraxJwtAuth<TResolver>(
            BuildCognitoAuthority(region, userPoolId),
            audience
        );
    }

    // ── Authority-string builders (internal for test coverage) ───────────

    internal static string BuildAuth0Authority(string domain)
    {
        var stripped = domain.Trim();
        if (stripped.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            stripped = stripped["https://".Length..];
        else if (stripped.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            stripped = stripped["http://".Length..];
        stripped = stripped.TrimEnd('/');
        return $"https://{stripped}/";
    }

    internal static string BuildEntraAuthority(string tenantId) =>
        $"https://login.microsoftonline.com/{tenantId}/v2.0";

    internal static string BuildCognitoAuthority(string region, string userPoolId) =>
        $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";
}
