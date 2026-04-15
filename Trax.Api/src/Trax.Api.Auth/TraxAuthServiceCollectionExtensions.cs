using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Trax.Api.Auth;

/// <summary>
/// Service-collection extensions shared by every Trax authentication scheme.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class TraxAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TraxPrincipal"/> as a scoped service that resolves
    /// the current HTTP request's authenticated principal. Junctions, services,
    /// and handlers can then inject <c>TraxPrincipal</c> directly without
    /// touching <see cref="IHttpContextAccessor"/>.
    /// </summary>
    /// <remarks>
    /// Idempotent: safe to call from multiple Trax auth schemes (<c>AddTraxApiKeyAuth</c>,
    /// future <c>AddTraxJwtAuth</c>, etc.) in the same app. Uses
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection, ServiceDescriptor)"/>
    /// semantics so the first registration wins.
    /// <para>
    /// Throws <see cref="TraxPrincipalNotAvailableException"/> when resolved
    /// from an execution path without an authenticated Trax principal (anonymous
    /// request, scheduler, background service). Junctions gated by <c>[TraxAuthorize]</c>
    /// can inject <c>TraxPrincipal</c> unconditionally and trust the upstream
    /// authorization check.
    /// </para>
    /// NO WARRANTY. See SECURITY-DISCLAIMER.md.
    /// </remarks>
    public static IServiceCollection AddTraxPrincipalAccessor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddScoped<TraxPrincipal>(sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            var user = accessor.HttpContext?.User;
            if (user is not null && user.TryGetTraxPrincipal(out var principal))
                return principal;

            throw new TraxPrincipalNotAvailableException();
        });
        return services;
    }
}
