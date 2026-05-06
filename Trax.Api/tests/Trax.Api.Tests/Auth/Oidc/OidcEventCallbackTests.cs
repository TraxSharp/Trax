using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NUnit.Framework;
using Trax.Api.Auth;
using Trax.Api.Auth.Oidc;

namespace Trax.Api.Tests.Auth.Oidc;

/// <summary>
/// Tests that drive the OIDC cookie and token-validated event callbacks
/// directly. The default Add tests only check the events are wired; these
/// invoke them and verify the documented behaviours (401/403 redirects,
/// principal resolution, failure paths).
/// </summary>
[TestFixture]
public class OidcEventCallbackTests
{
    private static IServiceProvider BuildProvider(Action<OidcBuilder>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTraxOidcAuth(b =>
        {
            b.UseAuthority("https://id.example.com", "client").AllowHttpMetadata();
            extra?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    private static OpenIdConnectOptions GetOidcOptions(IServiceProvider sp) =>
        sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OidcDefaults.SchemeName);

    private static CookieAuthenticationOptions GetCookieOptions(IServiceProvider sp) =>
        sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(OidcDefaults.CookieSchemeName);

    [Test]
    public async Task OnRedirectToLogin_SetsStatus401()
    {
        using var sp = (ServiceProvider)BuildProvider();
        var opts = GetCookieOptions(sp);
        var http = new DefaultHttpContext { RequestServices = sp };

        var ctx = new RedirectContext<CookieAuthenticationOptions>(
            http,
            new AuthenticationScheme(
                OidcDefaults.CookieSchemeName,
                null,
                typeof(CookieAuthenticationHandler)
            ),
            opts,
            new AuthenticationProperties(),
            "/login"
        );

        await opts.Events.OnRedirectToLogin(ctx);

        http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task OnRedirectToAccessDenied_SetsStatus403()
    {
        using var sp = (ServiceProvider)BuildProvider();
        var opts = GetCookieOptions(sp);
        var http = new DefaultHttpContext { RequestServices = sp };

        var ctx = new RedirectContext<CookieAuthenticationOptions>(
            http,
            new AuthenticationScheme(
                OidcDefaults.CookieSchemeName,
                null,
                typeof(CookieAuthenticationHandler)
            ),
            opts,
            new AuthenticationProperties(),
            "/denied"
        );

        await opts.Events.OnRedirectToAccessDenied(ctx);

        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task OnTokenValidated_NullPrincipal_FailsContext()
    {
        using var sp = (ServiceProvider)BuildProvider();
        var opts = GetOidcOptions(sp);
        var ctx = NewTokenValidatedContext(sp, opts, principal: null);

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure.Should().NotBeNull();
        ctx.Result.Failure!.Message.Should().Contain("without a principal");
    }

    [Test]
    public async Task OnTokenValidated_ResolverReturnsNull_FailsContext()
    {
        using var sp = (ServiceProvider)BuildResolverProvider(new StubResolver(returns: null));
        var opts = GetOidcOptions(sp);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "user-1")], "test")
        );
        var ctx = NewTokenValidatedContext(sp, opts, principal);

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("known Trax principal");
    }

    [Test]
    public async Task OnTokenValidated_ResolverThrows_FailsContextWithException()
    {
        var boom = new InvalidOperationException("resolver explosion");
        using var sp = (ServiceProvider)BuildResolverProvider(new StubResolver(throws: boom));
        var opts = GetOidcOptions(sp);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "user-1")], "test")
        );
        var ctx = NewTokenValidatedContext(sp, opts, principal);

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure.Should().BeSameAs(boom);
    }

    [Test]
    public async Task OnTokenValidated_ResolverReturnsPrincipal_ReplacesPrincipal()
    {
        var traxPrincipal = new TraxPrincipal(
            Id: "u-1",
            DisplayName: "User One",
            Roles: ["admin"],
            Claims: null,
            PrincipalType: OidcDefaults.PrincipalType
        );
        using var sp = (ServiceProvider)BuildResolverProvider(
            new StubResolver(returns: traxPrincipal)
        );
        var opts = GetOidcOptions(sp);
        var inputPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "u-1")], "test")
        );
        var ctx = NewTokenValidatedContext(sp, opts, inputPrincipal);

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().BeNull();
        ctx.Principal.Should().NotBeSameAs(inputPrincipal);
        ctx.Principal!.FindFirst(TraxAuthClaimTypes.PrincipalId)?.Value.Should().Be("u-1");
    }

    [Test]
    public async Task OnTokenValidated_NoResolverRegistered_FailsContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTraxOidcAuth(b =>
            b.UseAuthority("https://id.example.com", "client").AllowHttpMetadata()
        );

        // Strip the registered resolver so the lookup at runtime returns null.
        var resolverDescriptor = services.Single(sd =>
            sd.ServiceType == typeof(ITraxPrincipalResolver<OidcTokenInput>)
        );
        services.Remove(resolverDescriptor);

        using var sp = services.BuildServiceProvider();
        var opts = GetOidcOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewTokenValidatedContext(sp, opts, principal);

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("ITraxPrincipalResolver<OidcTokenInput>");
    }

    [Test]
    public async Task OnTokenValidated_ExistingHandlerSetsResult_ShortCircuits()
    {
        // Wire a customizer that installs an OnTokenValidated which sets
        // Result. Trax's wrapper must defer to it and stop.
        using var sp = (ServiceProvider)BuildResolverProvider(
            new StubResolver(throws: new Exception("should not be called")),
            customize: o =>
            {
                o.Events.OnTokenValidated = c =>
                {
                    c.HandleResponse();
                    return Task.CompletedTask;
                };
            }
        );
        var opts = GetOidcOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewTokenValidatedContext(sp, opts, principal);

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Handled.Should().BeTrue();
    }

    private static IServiceProvider BuildResolverProvider(
        ITraxPrincipalResolver<OidcTokenInput> resolver,
        Action<OpenIdConnectOptions>? customize = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTraxOidcAuth(b =>
        {
            b.UseAuthority("https://id.example.com", "client").AllowHttpMetadata();
            if (customize is not null)
                b.CustomizeOidcOptions(customize);
        });

        // Replace the resolver registered by AddTraxOidcAuth.
        var existing = services.Single(sd =>
            sd.ServiceType == typeof(ITraxPrincipalResolver<OidcTokenInput>)
        );
        services.Remove(existing);
        services.AddSingleton<ITraxPrincipalResolver<OidcTokenInput>>(resolver);

        return services.BuildServiceProvider();
    }

    private static TokenValidatedContext NewTokenValidatedContext(
        IServiceProvider sp,
        OpenIdConnectOptions opts,
        ClaimsPrincipal? principal
    )
    {
        var http = new DefaultHttpContext { RequestServices = sp };
        var scheme = new AuthenticationScheme(
            OidcDefaults.SchemeName,
            null,
            typeof(OpenIdConnectHandler)
        );
        var ctx = new TokenValidatedContext(
            http,
            scheme,
            opts,
            principal ?? new ClaimsPrincipal(new ClaimsIdentity()),
            new AuthenticationProperties()
        )
        {
            ProtocolMessage = new OpenIdConnectMessage
            {
                IdToken = "id-token",
                AccessToken = "access-token",
            },
        };
        if (principal is null)
            ctx.Principal = null;
        return ctx;
    }

    private sealed class StubResolver : ITraxPrincipalResolver<OidcTokenInput>
    {
        private readonly TraxPrincipal? _returns;
        private readonly Exception? _throws;

        public StubResolver(TraxPrincipal? returns = null, Exception? throws = null)
        {
            _returns = returns;
            _throws = throws;
        }

        public ValueTask<TraxPrincipal?> ResolveAsync(OidcTokenInput input, CancellationToken ct)
        {
            if (_throws is not null)
                throw _throws;
            return ValueTask.FromResult(_returns);
        }
    }
}
