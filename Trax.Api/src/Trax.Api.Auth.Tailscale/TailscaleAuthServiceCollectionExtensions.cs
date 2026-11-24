using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// Service-collection extensions that register the Trax tailnet sign-in scheme, its authorization policy, its place in
/// the combined <c>TraxAuthPolicy</c>, and the startup check that refuses a host not exposed only on a tailnet.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-auth/add-trax-tailscale-auth">SDK reference</seealso>
public static class TailscaleAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Trax tailnet sign-in scheme: a caller is the login Tailscale serve names in
    /// <c>Tailscale-User-Login</c>, with the roles <paramref name="configure"/> maps it to, but only on a request that
    /// carries the proxy's secret. The host refuses to start unless its deployment set
    /// <see cref="DeployExposure.ConfigurationKey"/> to <see cref="DeployExposure.Tailnet"/> and the secret is
    /// configured.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Names the logins admitted and their roles, through <see cref="TailscaleBuilder"/>.</param>
    /// <param name="configureOptions">Optional hook to set <see cref="TailscaleAuthenticationOptions.ProxySecret"/> from somewhere other than configuration.</param>
    /// <returns>The underlying <see cref="AuthenticationBuilder"/> so callers can chain additional schemes.</returns>
    /// <remarks>
    /// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
    /// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
    /// </remarks>
    public static AuthenticationBuilder AddTraxTailscaleAuth(
        this IServiceCollection services,
        Action<TailscaleBuilder> configure,
        Action<TailscaleAuthenticationOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new TailscaleBuilder();
        configure(builder);
        services.AddSingleton(builder.Build());
        services.AddTraxPrincipalAccessor();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, TailscaleTrustValidator>()
        );
        services
            .AddOptions<TailscaleAuthenticationOptions>(TailscaleDefaults.SchemeName)
            .PostConfigure<IConfiguration>(
                (options, configuration) =>
                    options.ProxySecret ??= configuration[DeployExposure.ProxySecretKey]
            );

        var authBuilder = services.AddAuthentication();
        authBuilder.AddScheme<TailscaleAuthenticationOptions, TailscaleAuthHandler>(
            TailscaleDefaults.SchemeName,
            configureOptions ?? (_ => { })
        );

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                TailscaleDefaults.PolicyName,
                policy =>
                    policy
                        .AddAuthenticationSchemes(TailscaleDefaults.SchemeName)
                        .RequireAuthenticatedUser()
                        .AddRequirements(new TraxSchemeRequirement(TailscaleDefaults.SchemeName))
            );

        services.PostConfigure<AuthorizationOptions>(opts =>
        {
            var existing = opts.GetPolicy(TraxAuthClaimTypes.TraxAuthPolicy);
            var schemes = existing?.AuthenticationSchemes.ToList() ?? [];
            if (!schemes.Contains(TailscaleDefaults.SchemeName))
                schemes.Add(TailscaleDefaults.SchemeName);

            opts.AddPolicy(
                TraxAuthClaimTypes.TraxAuthPolicy,
                new AuthorizationPolicyBuilder([.. schemes]).RequireAuthenticatedUser().Build()
            );
        });

        return authBuilder;
    }
}
