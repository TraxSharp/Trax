using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.Auth.Oidc;

/// <summary>
/// Service-collection extensions that register the Trax OpenID Connect scheme,
/// its session cookie, the <see cref="OidcDefaults.PolicyName"/> authorization
/// policy, the combined <c>TraxAuthPolicy</c>, and supporting services
/// (<see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>, startup
/// disclaimer log).
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// This package wires the browser-facing OIDC code flow. Authentication state
/// after the callback lives in the session cookie
/// (<see cref="OidcDefaults.CookieSchemeName"/>); that cookie is what
/// subsequent GraphQL and MVC requests authenticate against. For token-based
/// API clients, use <c>Trax.Api.Auth.Jwt</c>.
/// </para>
/// </remarks>
public static class OidcAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Trax OIDC scheme against an identity provider, using
    /// <see cref="DefaultOidcPrincipalResolver"/> and default scopes
    /// (<c>openid</c>, <c>profile</c>). Equivalent to
    /// <c>AddTraxOidcAuth(oidc =&gt; oidc.UseAuthority(authority, clientId))</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="authority">OIDC issuer URL. Must support the discovery document at <c>{authority}/.well-known/openid-configuration</c>.</param>
    /// <param name="clientId">Registered client id.</param>
    /// <returns>The underlying <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddTraxOidcAuth(
        this IServiceCollection services,
        string authority,
        string clientId
    ) => services.AddTraxOidcAuth(oidc => oidc.UseAuthority(authority, clientId));

    /// <summary>
    /// Registers the Trax OIDC scheme against an identity provider, resolving
    /// principals via a DI-managed <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxOidcAuth<TResolver>(
        this IServiceCollection services,
        string authority,
        string clientId
    )
        where TResolver : class, ITraxPrincipalResolver<OidcTokenInput> =>
        services.AddTraxOidcAuth<TResolver>(oidc => oidc.UseAuthority(authority, clientId));

    /// <summary>
    /// Registers the Trax OIDC scheme using <see cref="DefaultOidcPrincipalResolver"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback that configures authority, client id, scopes, and callback paths via <see cref="OidcBuilder"/>.</param>
    /// <returns>The underlying <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddTraxOidcAuth(
        this IServiceCollection services,
        Action<OidcBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddSingleton<
            ITraxPrincipalResolver<OidcTokenInput>,
            DefaultOidcPrincipalResolver
        >();
        return AddCore(services, configure);
    }

    /// <summary>
    /// Registers the Trax OIDC scheme, resolving principals via a DI-managed
    /// <typeparamref name="TResolver"/>.
    /// </summary>
    public static AuthenticationBuilder AddTraxOidcAuth<TResolver>(
        this IServiceCollection services,
        Action<OidcBuilder> configure
    )
        where TResolver : class, ITraxPrincipalResolver<OidcTokenInput>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddScoped<ITraxPrincipalResolver<OidcTokenInput>, TResolver>();
        return AddCore(services, configure);
    }

    private static AuthenticationBuilder AddCore(
        IServiceCollection services,
        Action<OidcBuilder> configure
    )
    {
        var oidcBuilder = new OidcBuilder();
        configure(oidcBuilder);
        oidcBuilder.Validate();

        services.AddTraxPrincipalAccessor();
        EnsureDisclaimerLog(services);

        var authBuilder = services.AddAuthentication();
        authBuilder.AddCookie(
            OidcDefaults.CookieSchemeName,
            options => ConfigureCookie(options, oidcBuilder)
        );
        authBuilder.AddOpenIdConnect(
            OidcDefaults.SchemeName,
            options => ConfigureOidc(options, oidcBuilder)
        );

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                OidcDefaults.PolicyName,
                policy =>
                    policy
                        .AddAuthenticationSchemes(OidcDefaults.CookieSchemeName)
                        .RequireAuthenticatedUser()
            );

        services.PostConfigure<AuthorizationOptions>(opts =>
        {
            var existing = opts.GetPolicy(TraxAuthClaimTypes.TraxAuthPolicy);
            var schemes = existing?.AuthenticationSchemes.ToList() ?? [];
            if (!schemes.Contains(OidcDefaults.CookieSchemeName))
                schemes.Add(OidcDefaults.CookieSchemeName);

            opts.AddPolicy(
                TraxAuthClaimTypes.TraxAuthPolicy,
                new AuthorizationPolicyBuilder([.. schemes]).RequireAuthenticatedUser().Build()
            );
        });

        return authBuilder;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, OidcBuilder oidc)
    {
        options.Cookie.Name = "trax.oidc";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        // Trax GraphQL / API hosts want 401 on unauthenticated calls, not a
        // 302 to /Account/Login (which doesn't exist in an API). Applications
        // that embed an MVC login page can override via CustomizeCookieOptions.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        oidc.CookieOptionsCustomizer?.Invoke(options);
    }

    private static void ConfigureOidc(OpenIdConnectOptions options, OidcBuilder oidc)
    {
        options.Authority = oidc.Authority;
        options.ClientId = oidc.ClientId;
        options.ClientSecret = oidc.ClientSecret;
        options.ResponseType = oidc.ResponseType;
        options.UsePkce = oidc.UsePkce;
        options.SaveTokens = oidc.SaveTokens;
        options.CallbackPath = oidc.CallbackPath;
        options.SignedOutCallbackPath = oidc.SignedOutCallbackPath;
        options.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
        options.SignInScheme = OidcDefaults.CookieSchemeName;
        options.SignOutScheme = OidcDefaults.CookieSchemeName;

        options.Scope.Clear();
        foreach (var scope in oidc.Scopes)
            options.Scope.Add(scope);

        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.RequireSignedTokens = true;

        options.Events ??= new OpenIdConnectEvents();
        var existingValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            if (existingValidated is not null)
                await existingValidated(context);

            if (context.Result is not null)
                return;

            if (context.Principal is null)
            {
                context.Fail("OIDC validation completed without a principal.");
                return;
            }

            var resolver = context.HttpContext.RequestServices.GetService<
                ITraxPrincipalResolver<OidcTokenInput>
            >();
            if (resolver is null)
            {
                context.Fail(
                    "No ITraxPrincipalResolver<OidcTokenInput> registered. "
                        + "Call AddTraxOidcAuth before UseAuthentication."
                );
                return;
            }

            TraxPrincipal? traxPrincipal;
            try
            {
                var input = new OidcTokenInput(
                    context.Principal,
                    context.ProtocolMessage?.IdToken,
                    context.ProtocolMessage?.AccessToken
                );
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
                context.Fail("OIDC id-token did not map to a known Trax principal.");
                return;
            }

            context.Principal = traxPrincipal.ToClaimsPrincipal(OidcDefaults.SchemeName);
        };

        oidc.OidcOptionsCustomizer?.Invoke(options);
    }

    private static void EnsureDisclaimerLog(IServiceCollection services)
    {
        if (
            services.Any(sd => sd.ImplementationType == typeof(TraxOidcAuthDisclaimerHostedService))
        )
            return;

        services.AddSingleton<IHostedService, TraxOidcAuthDisclaimerHostedService>();
    }
}
