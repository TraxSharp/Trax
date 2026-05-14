using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Service-collection extensions that register Trax JWT bearer authentication
/// schemes, their authorization policies, the combined <c>TraxAuthPolicy</c>,
/// and supporting services (<see cref="IHttpContextAccessor"/>, startup
/// disclaimer log).
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Token validation (signature, issuer, audience, lifetime) is delegated to
/// <see cref="JwtBearerHandler"/>. After validation, Trax's
/// <c>OnTokenValidated</c> hook runs an
/// <see cref="ITraxPrincipalResolver{JwtTokenInput}"/> to project the token
/// into a <see cref="TraxPrincipal"/>. A resolver that returns <c>null</c>
/// fails authentication.
/// </para>
/// <para>
/// All overloads accept an optional scheme name. Hosts that register more
/// than one JWT issuer (for example, a customer-facing identity provider
/// plus an internal service-to-service token) call <c>AddTraxJwtAuth</c>
/// multiple times with distinct scheme names, then optionally wire
/// <c>AddTraxJwtDispatcher</c> to route inbound tokens by their <c>iss</c>
/// claim. Single-issuer hosts can ignore scheme names entirely and use the
/// short-form overloads that default to <see cref="JwtDefaults.SchemeName"/>.
/// </para>
/// </remarks>
public static class JwtAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Trax JWT scheme against an OIDC authority under
    /// <see cref="JwtDefaults.SchemeName"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxJwtAuth(
        this IServiceCollection services,
        string authority,
        string audience
    ) => services.AddTraxJwtAuth(jwt => jwt.UseAuthority(authority, audience));

    /// <summary>
    /// Registers the Trax JWT scheme against an OIDC authority under
    /// <see cref="JwtDefaults.SchemeName"/>, resolving principals via a
    /// DI-managed <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
        this IServiceCollection services,
        string authority,
        string audience
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput> =>
        services.AddTraxJwtAuth<TResolver>(jwt => jwt.UseAuthority(authority, audience));

    /// <summary>
    /// Registers the Trax JWT scheme using <see cref="DefaultJwtPrincipalResolver"/>
    /// under <see cref="JwtDefaults.SchemeName"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxJwtAuth(
        this IServiceCollection services,
        Action<JwtBuilder> configure
    ) => services.AddTraxJwtAuth(JwtDefaults.SchemeName, configure);

    /// <summary>
    /// Registers the Trax JWT scheme under <see cref="JwtDefaults.SchemeName"/>,
    /// resolving principals via a DI-managed <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
        this IServiceCollection services,
        Action<JwtBuilder> configure
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput> =>
        services.AddTraxJwtAuth<TResolver>(JwtDefaults.SchemeName, configure);

    /// <summary>
    /// Registers a Trax JWT scheme under the supplied name using
    /// <see cref="DefaultJwtPrincipalResolver"/>. Hosts that accept tokens
    /// from multiple issuers call this overload more than once with distinct
    /// scheme names.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schemeName">
    /// Authentication scheme name. Must be unique across all schemes
    /// registered on this <see cref="IServiceCollection"/>. The associated
    /// authorization policy is named <c>{schemeName}-JwtPolicy</c>.
    /// </param>
    /// <param name="configure">Configures issuer, audience, and key source.</param>
    public static AuthenticationBuilder AddTraxJwtAuth(
        this IServiceCollection services,
        string schemeName,
        Action<JwtBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton<DefaultJwtPrincipalResolver>();
        var registry = JwtResolverRegistry.GetOrAdd(services);
        registry.Register(schemeName, sp => sp.GetRequiredService<DefaultJwtPrincipalResolver>());

        if (schemeName == JwtDefaults.SchemeName)
            services.TryAddSingleton<
                ITraxPrincipalResolver<JwtTokenInput>,
                DefaultJwtPrincipalResolver
            >();

        return AddCore(services, schemeName, configure);
    }

    /// <summary>
    /// Registers a Trax JWT scheme under the supplied name, resolving
    /// principals via a DI-managed <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
        this IServiceCollection services,
        string schemeName,
        Action<JwtBuilder> configure
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddScoped<TResolver>();
        var registry = JwtResolverRegistry.GetOrAdd(services);
        registry.Register(schemeName, sp => sp.GetRequiredService<TResolver>());

        if (schemeName == JwtDefaults.SchemeName)
            services.TryAddScoped<ITraxPrincipalResolver<JwtTokenInput>, TResolver>();

        return AddCore(services, schemeName, configure);
    }

    /// <summary>
    /// Registers a policy authentication scheme that inspects the inbound
    /// Bearer token's <c>iss</c> claim and forwards authentication to the
    /// matching JWT scheme. Hosts that gate endpoints on the dispatcher
    /// scheme name accept tokens from any registered issuer without
    /// explicitly enumerating schemes per endpoint.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Configures issuer-to-scheme mappings via <see cref="JwtDispatcherBuilder"/>.
    /// </param>
    /// <returns>The underlying <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddTraxJwtDispatcher(
        this IServiceCollection services,
        Action<JwtDispatcherBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new JwtDispatcherBuilder();
        configure(builder);
        builder.Validate();

        if (IsSchemeRegistered(services, builder.SchemeName))
            throw new InvalidOperationException(
                $"A scheme named '{builder.SchemeName}' is already registered. "
                    + "Call WithSchemeName(...) on the dispatcher builder to pick a different name."
            );

        var authBuilder = services.AddAuthentication();

        if (!IsSchemeRegistered(services, JwtDefaults.RejectSchemeName))
        {
            services.AddSingleton(new SchemeMarker(JwtDefaults.RejectSchemeName));
            authBuilder.AddScheme<AuthenticationSchemeOptions, TraxJwtRejectAuthenticationHandler>(
                JwtDefaults.RejectSchemeName,
                _ => { }
            );
        }

        var fallback = builder.FallbackSchemeName ?? JwtDefaults.RejectSchemeName;
        var mappings = new Dictionary<string, string>(builder.Mappings, StringComparer.Ordinal);
        var dispatcherSchemeName = builder.SchemeName;

        authBuilder.AddPolicyScheme(
            dispatcherSchemeName,
            displayName: dispatcherSchemeName,
            options =>
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    var header = ctx.Request.Headers.Authorization.ToString();
                    var token = JwtIssuerPeek.TryReadBearerToken(header);
                    if (token is null)
                        return fallback;
                    var issuer = JwtIssuerPeek.TryReadIssuer(token);
                    if (issuer is null)
                        return fallback;
                    return mappings.TryGetValue(issuer, out var scheme) ? scheme : fallback;
                };
            }
        );

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                dispatcherSchemeName + JwtDefaults.PolicyNameSuffix,
                policy =>
                    policy.AddAuthenticationSchemes(dispatcherSchemeName).RequireAuthenticatedUser()
            );

        IncludeInTraxAuthPolicy(services, dispatcherSchemeName);

        return authBuilder;
    }

    private static bool IsSchemeRegistered(IServiceCollection services, string schemeName) =>
        services.Any(sd =>
            sd.ServiceType == typeof(SchemeMarker)
            && sd.ImplementationInstance is SchemeMarker marker
            && marker.Name == schemeName
        );

    private static AuthenticationBuilder AddCore(
        IServiceCollection services,
        string schemeName,
        Action<JwtBuilder> configure
    )
    {
        var jwtBuilder = new JwtBuilder();
        configure(jwtBuilder);
        jwtBuilder.Validate();

        services.AddTraxPrincipalAccessor();
        EnsureDisclaimerLog(services);

        var authBuilder = services.AddAuthentication();
        authBuilder.AddJwtBearer(schemeName, options => ConfigureJwtBearer(options, jwtBuilder));

        var policyName = ResolvePolicyName(schemeName);

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                policyName,
                policy => policy.AddAuthenticationSchemes(schemeName).RequireAuthenticatedUser()
            );

        IncludeInTraxAuthPolicy(services, schemeName);

        return authBuilder;
    }

    private static string ResolvePolicyName(string schemeName) =>
        schemeName == JwtDefaults.SchemeName
            ? JwtDefaults.PolicyName
            : schemeName + JwtDefaults.PolicyNameSuffix;

    private static void IncludeInTraxAuthPolicy(IServiceCollection services, string schemeName)
    {
        if (!IsSchemeRegistered(services, schemeName))
            services.AddSingleton(new SchemeMarker(schemeName));
        services.PostConfigure<AuthorizationOptions>(opts =>
        {
            var existing = opts.GetPolicy(TraxAuthClaimTypes.TraxAuthPolicy);
            var schemes = existing?.AuthenticationSchemes.ToList() ?? [];
            if (!schemes.Contains(schemeName))
                schemes.Add(schemeName);

            opts.AddPolicy(
                TraxAuthClaimTypes.TraxAuthPolicy,
                new AuthorizationPolicyBuilder([.. schemes]).RequireAuthenticatedUser().Build()
            );
        });
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options, JwtBuilder jwtBuilder)
    {
        options.RequireHttpsMetadata = jwtBuilder.RequireHttpsMetadata;

        if (jwtBuilder.Authority is not null)
        {
            options.Authority = jwtBuilder.Authority;
            options.Audience = jwtBuilder.Audience;
        }

        var tvp = options.TokenValidationParameters;
        tvp.ValidateIssuer = true;
        tvp.ValidateAudience = true;
        tvp.ValidateLifetime = true;
        tvp.ValidateIssuerSigningKey = true;
        tvp.RequireSignedTokens = true;
        tvp.RequireExpirationTime = true;

        if (jwtBuilder.Issuer is not null)
            tvp.ValidIssuer = jwtBuilder.Issuer;
        if (jwtBuilder.Audience is not null)
            tvp.ValidAudience = jwtBuilder.Audience;
        if (jwtBuilder.SigningKey is not null)
            tvp.IssuerSigningKey = jwtBuilder.SigningKey;
        if (jwtBuilder.ClockSkew is { } skew)
            tvp.ClockSkew = skew;

        jwtBuilder.TokenValidationCustomizer?.Invoke(tvp);

        options.Events ??= new JwtBearerEvents();
        var existingValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            if (existingValidated is not null)
                await existingValidated(context);

            if (context.Result is not null)
                return;

            if (context.Principal is null || context.SecurityToken is null)
            {
                context.Fail("JWT validation completed without a principal or security token.");
                return;
            }

            var sp = context.HttpContext.RequestServices;
            var schemeName = context.Scheme.Name;

            ITraxPrincipalResolver<JwtTokenInput>? resolver;
            try
            {
                resolver = ResolvePrincipalResolver(sp, schemeName);
            }
            catch (Exception ex)
            {
                context.Fail(ex);
                return;
            }
            if (resolver is null)
            {
                context.Fail(
                    schemeName == JwtDefaults.SchemeName
                        ? "No ITraxPrincipalResolver<JwtTokenInput> registered. "
                            + "Call AddTraxJwtAuth before UseAuthentication."
                        : $"No ITraxPrincipalResolver<JwtTokenInput> registered for scheme '{schemeName}'."
                );
                return;
            }

            TraxPrincipal? traxPrincipal;
            try
            {
                var input = new JwtTokenInput(context.Principal, context.SecurityToken);
                traxPrincipal = await resolver.ResolveAsync(
                    input,
                    context.HttpContext.RequestAborted
                );
            }
            catch (Exception ex)
            {
                context.Fail(ex);
                return;
            }

            if (traxPrincipal is null)
            {
                context.Fail("JWT did not map to a known Trax principal.");
                return;
            }

            context.Principal = traxPrincipal.ToClaimsPrincipal(context.Scheme.Name);
        };

        jwtBuilder.BearerOptionsCustomizer?.Invoke(options);
    }

    private static ITraxPrincipalResolver<JwtTokenInput>? ResolvePrincipalResolver(
        IServiceProvider sp,
        string schemeName
    )
    {
        // Default scheme: prefer a DI-level registration. This is how the
        // single-scheme API has always worked, and how hosts swap in mocks
        // or alternative resolvers via standard ServiceCollection edits.
        // Falls back to the registry's default-scheme entry when the DI
        // registration has been removed but a resolver type is still bound
        // to the registry (the legacy "RemoveAll then no replacement" path
        // resolves to null and the caller fails the auth, matching the
        // original behavior).
        if (schemeName == JwtDefaults.SchemeName)
            return sp.GetService<ITraxPrincipalResolver<JwtTokenInput>>();

        var registry = sp.GetService<JwtResolverRegistry>();
        return registry?.TryResolve(schemeName, sp);
    }

    private static void EnsureDisclaimerLog(IServiceCollection services)
    {
        if (services.Any(sd => sd.ImplementationType == typeof(TraxJwtAuthDisclaimerHostedService)))
            return;

        services.AddSingleton<IHostedService, TraxJwtAuthDisclaimerHostedService>();
    }

    // Sentinel registered per scheme so IncludeInTraxAuthPolicy can detect
    // duplicate registrations without building the service provider.
    private sealed class SchemeMarker(string name)
    {
        public string Name { get; } = name;
    }
}
