using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Api.Auth;
using Trax.Api.Auth.Oidc;

namespace Trax.Api.Tests.Auth.Oidc;

[TestFixture]
public class AddTraxOidcAuthTests
{
    [Test]
    public void AddTraxOidcAuth_StringOverload_RegistersAuthenticationAndDefaultResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        var builder = services.AddTraxOidcAuth("https://login.example.com", "client-id");

        builder.Should().NotBeNull();

        using var provider = services.BuildServiceProvider();
        provider
            .GetService<ITraxPrincipalResolver<OidcTokenInput>>()
            .Should()
            .BeOfType<DefaultOidcPrincipalResolver>();

        var policy = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var traxPolicy = policy
            .GetPolicyAsync(TraxAuthClaimTypes.TraxAuthPolicy)
            .GetAwaiter()
            .GetResult();
        traxPolicy.Should().NotBeNull();
    }

    [Test]
    public void AddTraxOidcAuth_BuilderOverload_AppliesAuthorityAndClientId()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        var builder = services.AddTraxOidcAuth(oidc =>
            oidc.UseAuthority("https://login.example.com", "client-id")
        );

        builder.Should().NotBeNull();
        using var provider = services.BuildServiceProvider();
        provider.GetService<ITraxPrincipalResolver<OidcTokenInput>>().Should().NotBeNull();
    }

    [Test]
    public void AddTraxOidcAuth_NullServices_Throws()
    {
        IServiceCollection services = null!;

        Action act = () => services.AddTraxOidcAuth(_ => { });

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddTraxOidcAuth_NullConfigure_Throws()
    {
        var services = new ServiceCollection();
        Action<OidcBuilder> configure = null!;

        Action act = () => services.AddTraxOidcAuth(configure);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddTraxOidcAuth_GenericResolver_RegistersCustomResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        var builder = services.AddTraxOidcAuth<CustomResolver>(
            "https://login.example.com",
            "client-id"
        );

        builder.Should().NotBeNull();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<ITraxPrincipalResolver<OidcTokenInput>>()
            .Should()
            .BeOfType<CustomResolver>();
    }

    [Test]
    public void AddTraxOidcAuth_GenericResolverWithBuilder_RegistersResolverAndConfig()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        var builder = services.AddTraxOidcAuth<CustomResolver>(oidc =>
            oidc.UseAuthority("https://login.example.com", "client-id")
        );

        builder.Should().NotBeNull();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<ITraxPrincipalResolver<OidcTokenInput>>()
            .Should()
            .BeOfType<CustomResolver>();
    }

    [Test]
    public void AddTraxOidcAuth_GenericResolverWithNullServices_Throws()
    {
        IServiceCollection services = null!;
        Action act = () => services.AddTraxOidcAuth<CustomResolver>(_ => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddTraxOidcAuth_GenericResolverWithNullConfigure_Throws()
    {
        var services = new ServiceCollection();
        Action<OidcBuilder> configure = null!;
        Action act = () => services.AddTraxOidcAuth<CustomResolver>(configure);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddTraxOidcAuth_TwiceInSameCollection_DisclaimerLogRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        services.AddTraxOidcAuth("https://a.example", "client-a");
        services.AddTraxOidcAuth("https://b.example", "client-b");

        var disclaimerCount = services.Count(sd =>
            sd.ImplementationType == typeof(TraxOidcAuthDisclaimerHostedService)
        );
        disclaimerCount.Should().Be(1);
    }

    [Test]
    public void AddTraxOidcAuth_ConfiguresCookieOptionsWithRedirectHandlers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTraxOidcAuth("https://login.example.com", "client-id");
        using var provider = services.BuildServiceProvider();

        var cookieOptions = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(OidcDefaults.CookieSchemeName);

        cookieOptions.Cookie.Name.Should().Be("trax.oidc");
        cookieOptions.Cookie.HttpOnly.Should().BeTrue();
        cookieOptions.SlidingExpiration.Should().BeTrue();
        cookieOptions.Events.OnRedirectToLogin.Should().NotBeNull();
        cookieOptions.Events.OnRedirectToAccessDenied.Should().NotBeNull();
    }

    [Test]
    public void AddTraxOidcAuth_ConfiguresOidcOptionsWithAuthority()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTraxOidcAuth(oidc =>
            oidc.UseAuthority("https://login.example.com", "client-id")
                .WithClientSecret("shh")
                .AddScope("email")
                .AllowHttpMetadata()
        );
        using var provider = services.BuildServiceProvider();

        var oidcOptions = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>>()
            .Get(OidcDefaults.SchemeName);

        oidcOptions.Authority.Should().Be("https://login.example.com");
        oidcOptions.ClientId.Should().Be("client-id");
        oidcOptions.ClientSecret.Should().Be("shh");
        oidcOptions.RequireHttpsMetadata.Should().BeFalse();
        oidcOptions.Scope.Should().Contain("email");
        oidcOptions.Events.OnTokenValidated.Should().NotBeNull();
    }

    private class CustomResolver : ITraxPrincipalResolver<OidcTokenInput>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(OidcTokenInput input, CancellationToken ct) =>
            ValueTask.FromResult<TraxPrincipal?>(null);
    }
}
