using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.Auth.ApiKey;

/// <summary>
/// Service-collection extensions that register the Trax API-key authentication
/// scheme, its authorization policy, the combined <c>TraxAuthPolicy</c>, and
/// supporting services (<see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>,
/// startup disclaimer log).
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class ApiKeyAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Trax API-key authentication scheme with a static key set.
    /// Keys configured through the builder are salted and SHA-256 hashed at
    /// registration time and compared in constant time on every request.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback that registers keys via <see cref="ApiKeyBuilder.Add(string, string, string[])"/> or <see cref="ApiKeyBuilder.AddHashed(byte[], byte[], string, string[])"/>.</param>
    /// <param name="configureOptions">Optional hook to override <see cref="ApiKeyAuthenticationOptions.HeaderName"/>.</param>
    /// <returns>The underlying <see cref="AuthenticationBuilder"/> so callers can chain additional schemes.</returns>
    /// <remarks>
    /// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
    /// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
    /// </remarks>
    public static AuthenticationBuilder AddTraxApiKeyAuth(
        this IServiceCollection services,
        Action<ApiKeyBuilder> configure,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ApiKeyBuilder();
        configure(builder);
        var resolver = builder.Build();

        return AddTraxApiKeyAuthWithInstance(services, resolver, configureOptions);
    }

    /// <summary>
    /// Registers the Trax API-key authentication scheme, resolving principals via
    /// a DI-managed <typeparamref name="TResolver"/>. Use this overload when your
    /// resolver needs scoped dependencies (DbContext, IOptions, etc.).
    /// </summary>
    /// <typeparam name="TResolver">The consumer's resolver implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional hook to override <see cref="ApiKeyAuthenticationOptions.HeaderName"/>.</param>
    /// <returns>The underlying <see cref="AuthenticationBuilder"/> so callers can chain additional schemes.</returns>
    /// <remarks>
    /// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
    /// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
    /// </remarks>
    public static AuthenticationBuilder AddTraxApiKeyAuth<TResolver>(
        this IServiceCollection services,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null
    )
        where TResolver : class, ITraxPrincipalResolver<string>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ITraxPrincipalResolver<string>, TResolver>();
        return AddTraxApiKeyAuthCore(services, configureOptions);
    }

    /// <summary>
    /// Test-only / advanced entry point: register a pre-constructed resolver
    /// instance as a singleton. Not part of the public stable surface — use
    /// <see cref="AddTraxApiKeyAuth(IServiceCollection, Action{ApiKeyBuilder}, Action{ApiKeyAuthenticationOptions}?)"/>
    /// or <see cref="AddTraxApiKeyAuth{TResolver}(IServiceCollection, Action{ApiKeyAuthenticationOptions}?)"/>
    /// in application code.
    /// </summary>
    internal static AuthenticationBuilder AddTraxApiKeyAuthWithInstance(
        this IServiceCollection services,
        ITraxPrincipalResolver<string> resolver,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resolver);
        services.AddSingleton(resolver);
        return AddTraxApiKeyAuthCore(services, configureOptions);
    }

    private static AuthenticationBuilder AddTraxApiKeyAuthCore(
        IServiceCollection services,
        Action<ApiKeyAuthenticationOptions>? configureOptions
    )
    {
        services.AddTraxPrincipalAccessor();
        EnsureDisclaimerLog(services);

        var authBuilder = services.AddAuthentication();
        authBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthHandler>(
            ApiKeyDefaults.SchemeName,
            configureOptions ?? (_ => { })
        );

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                ApiKeyDefaults.PolicyName,
                policy =>
                    policy
                        .AddAuthenticationSchemes(ApiKeyDefaults.SchemeName)
                        .RequireAuthenticatedUser()
            );

        services.PostConfigure<AuthorizationOptions>(opts =>
        {
            var existing = opts.GetPolicy(TraxAuthClaimTypes.TraxAuthPolicy);
            var schemes = existing?.AuthenticationSchemes.ToList() ?? [];
            if (!schemes.Contains(ApiKeyDefaults.SchemeName))
                schemes.Add(ApiKeyDefaults.SchemeName);

            opts.AddPolicy(
                TraxAuthClaimTypes.TraxAuthPolicy,
                new AuthorizationPolicyBuilder([.. schemes]).RequireAuthenticatedUser().Build()
            );
        });

        return authBuilder;
    }

    private static void EnsureDisclaimerLog(IServiceCollection services)
    {
        if (
            services.Any(sd =>
                sd.ImplementationType == typeof(TraxApiKeyAuthDisclaimerHostedService)
            )
        )
            return;

        services.AddSingleton<IHostedService, TraxApiKeyAuthDisclaimerHostedService>();
    }
}
