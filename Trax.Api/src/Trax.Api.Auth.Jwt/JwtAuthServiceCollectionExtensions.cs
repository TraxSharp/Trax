using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Service-collection extensions that register the Trax JWT bearer authentication
/// scheme, its authorization policy, the combined <c>TraxAuthPolicy</c>, and
/// supporting services (<see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>,
/// startup disclaimer log).
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Token validation (signature, issuer, audience, lifetime) is delegated to
/// <see cref="Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler"/>.
/// After validation, Trax's <c>OnTokenValidated</c> hook runs an
/// <see cref="ITraxPrincipalResolver{JwtTokenInput}"/> to project the token
/// into a <see cref="TraxPrincipal"/>. A resolver that returns <c>null</c>
/// fails authentication.
/// </para>
/// </remarks>
public static class JwtAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Trax JWT scheme against an OIDC authority. Signing keys
    /// are fetched from the authority's JWKS endpoint and refreshed on the
    /// provider's cadence. Equivalent to
    /// <c>AddTraxJwtAuth(jwt =&gt; jwt.UseAuthority(authority, audience))</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="authority">Issuer URL. Must be HTTPS; call the builder overload and <c>AllowHttpMetadata()</c> for dev hosts.</param>
    /// <param name="audience">Expected <c>aud</c> claim value.</param>
    /// <returns>The underlying <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddTraxJwtAuth(
        this IServiceCollection services,
        string authority,
        string audience
    ) => services.AddTraxJwtAuth(jwt => jwt.UseAuthority(authority, audience));

    /// <summary>
    /// Registers the Trax JWT scheme against an OIDC authority, resolving
    /// principals via a DI-managed <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
        this IServiceCollection services,
        string authority,
        string audience
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput> =>
        services.AddTraxJwtAuth<TResolver>(jwt => jwt.UseAuthority(authority, audience));

    /// <summary>
    /// Registers the Trax JWT scheme using <see cref="DefaultJwtPrincipalResolver"/>,
    /// which maps standard JWT claims (<c>sub</c>, <c>name</c>, <c>role</c>) into
    /// a <see cref="TraxPrincipal"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback that configures issuer, audience, and key source via <see cref="JwtBuilder"/>.</param>
    /// <returns>The underlying <see cref="AuthenticationBuilder"/> so callers can chain additional schemes.</returns>
    public static AuthenticationBuilder AddTraxJwtAuth(
        this IServiceCollection services,
        Action<JwtBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddSingleton<ITraxPrincipalResolver<JwtTokenInput>, DefaultJwtPrincipalResolver>();
        return AddCore(services, configure);
    }

    /// <summary>
    /// Registers the Trax JWT scheme, resolving principals via a DI-managed
    /// <typeparamref name="TResolver"/>. Use when the resolver needs scoped
    /// dependencies (DbContext, cache, HTTP client).
    /// </summary>
    /// <typeparam name="TResolver">The consumer's resolver implementation.</typeparam>
    public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
        this IServiceCollection services,
        Action<JwtBuilder> configure
    )
        where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddScoped<ITraxPrincipalResolver<JwtTokenInput>, TResolver>();
        return AddCore(services, configure);
    }

    private static AuthenticationBuilder AddCore(
        IServiceCollection services,
        Action<JwtBuilder> configure
    )
    {
        var jwtBuilder = new JwtBuilder();
        configure(jwtBuilder);
        jwtBuilder.Validate();

        services.AddTraxPrincipalAccessor();
        EnsureDisclaimerLog(services);

        var authBuilder = services.AddAuthentication();
        authBuilder.AddJwtBearer(
            JwtDefaults.SchemeName,
            options => ConfigureJwtBearer(options, jwtBuilder)
        );

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                JwtDefaults.PolicyName,
                policy =>
                    policy
                        .AddAuthenticationSchemes(JwtDefaults.SchemeName)
                        .RequireAuthenticatedUser()
            );

        services.PostConfigure<AuthorizationOptions>(opts =>
        {
            var existing = opts.GetPolicy(TraxAuthClaimTypes.TraxAuthPolicy);
            var schemes = existing?.AuthenticationSchemes.ToList() ?? [];
            if (!schemes.Contains(JwtDefaults.SchemeName))
                schemes.Add(JwtDefaults.SchemeName);

            opts.AddPolicy(
                TraxAuthClaimTypes.TraxAuthPolicy,
                new AuthorizationPolicyBuilder([.. schemes]).RequireAuthenticatedUser().Build()
            );
        });

        return authBuilder;
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

            var resolver = context.HttpContext.RequestServices.GetService<
                ITraxPrincipalResolver<JwtTokenInput>
            >();
            if (resolver is null)
            {
                context.Fail(
                    "No ITraxPrincipalResolver<JwtTokenInput> registered. "
                        + "Call AddTraxJwtAuth before UseAuthentication."
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

            context.Principal = traxPrincipal.ToClaimsPrincipal(JwtDefaults.SchemeName);
        };

        jwtBuilder.BearerOptionsCustomizer?.Invoke(options);
    }

    private static void EnsureDisclaimerLog(IServiceCollection services)
    {
        if (services.Any(sd => sd.ImplementationType == typeof(TraxJwtAuthDisclaimerHostedService)))
            return;

        services.AddSingleton<IHostedService, TraxJwtAuthDisclaimerHostedService>();
    }
}
