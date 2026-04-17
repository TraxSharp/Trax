using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trax.Api.Auth;
using Trax.Api.Auth.Oidc;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class AddTraxOidcAuthTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        return services;
    }

    private static void ConfigureDefault(OidcBuilder b) =>
        b.UseAuthority("https://id.example.com", "my-client").AllowHttpMetadata();

    [Test]
    public void RegistersChallengeScheme()
    {
        var services = NewServices();

        services.AddTraxOidcAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        schemes
            .GetSchemeAsync(OidcDefaults.SchemeName)
            .GetAwaiter()
            .GetResult()
            .Should()
            .NotBeNull();
    }

    [Test]
    public void RegistersCookieScheme()
    {
        var services = NewServices();

        services.AddTraxOidcAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        schemes
            .GetSchemeAsync(OidcDefaults.CookieSchemeName)
            .GetAwaiter()
            .GetResult()
            .Should()
            .NotBeNull();
    }

    [Test]
    public void RegistersDefaultResolver()
    {
        var services = NewServices();

        services.AddTraxOidcAuth(ConfigureDefault);

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<OidcTokenInput>))
            .Which.ImplementationType.Should()
            .Be(typeof(DefaultOidcPrincipalResolver));
    }

    [Test]
    public void WithCustomResolverType_RegistersScoped()
    {
        var services = NewServices();

        services.AddTraxOidcAuth<TestResolver>(ConfigureDefault);

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<OidcTokenInput>))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task RegistersOidcPolicy_BoundToCookieScheme()
    {
        var services = NewServices();

        services.AddTraxOidcAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(OidcDefaults.PolicyName);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(OidcDefaults.CookieSchemeName);
        policy.AuthenticationSchemes.Should().NotContain(OidcDefaults.SchemeName);
    }

    [Test]
    public async Task CombinedTraxAuthPolicy_IncludesCookieScheme()
    {
        var services = NewServices();

        services.AddTraxOidcAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(TraxAuthClaimTypes.TraxAuthPolicy);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(OidcDefaults.CookieSchemeName);
    }

    [Test]
    public void EmptyConfigure_ThrowsActionable()
    {
        var services = NewServices();

        var act = () => services.AddTraxOidcAuth(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseAuthority*");
    }

    [Test]
    public void NullConfigure_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxOidcAuth((Action<OidcBuilder>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ScopesFlowToOptions()
    {
        var services = NewServices();

        services.AddTraxOidcAuth(b =>
            b.UseAuthority("https://id.example.com", "c")
                .AllowHttpMetadata()
                .AddScope("email")
                .AddScope("offline_access")
        );
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OidcDefaults.SchemeName);

        opts.Scope.Should().Contain("openid").And.Contain("email").And.Contain("offline_access");
        opts.ClientId.Should().Be("c");
        opts.Authority.Should().Be("https://id.example.com");
    }

    [Test]
    public void PkceEnabled_ByDefault()
    {
        var services = NewServices();

        services.AddTraxOidcAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OidcDefaults.SchemeName);

        opts.UsePkce.Should().BeTrue();
        opts.ResponseType.Should().Be("code");
    }

    [Test]
    public void CalledTwice_RegistersDisclaimerOnce()
    {
        var services = NewServices();

        services.AddTraxOidcAuth(ConfigureDefault);
        services.AddTraxOidcAuth(ConfigureDefault);

        var hostedCount = services.Count(sd =>
            sd.ServiceType == typeof(IHostedService)
            && sd.ImplementationType?.Name.Contains("Disclaimer") == true
        );

        hostedCount.Should().Be(1);
    }

    [Test]
    public async Task UnauthenticatedRequest_AgainstCookiePolicy_Returns401()
    {
        using var host = await CreateHost();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/protected");

        // Cookie scheme returns 401 when unauthenticated (doesn't redirect; OIDC
        // challenge is the thing that redirects).
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public void Positional_WiresAuthorityAndClientId()
    {
        var services = NewServices();

        services.AddTraxOidcAuth("https://id.example.com", "my-client");
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OidcDefaults.SchemeName);

        opts.Authority.Should().Be("https://id.example.com");
        opts.ClientId.Should().Be("my-client");
        opts.Scope.Should().Contain(new[] { "openid", "profile" });
        opts.UsePkce.Should().BeTrue();
    }

    [Test]
    public void Positional_UsesDefaultResolver()
    {
        var services = NewServices();

        services.AddTraxOidcAuth("https://id.example.com", "my-client");

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<OidcTokenInput>))
            .Which.ImplementationType.Should()
            .Be(typeof(DefaultOidcPrincipalResolver));
    }

    [Test]
    public void Positional_WithResolverType_RegistersScoped()
    {
        var services = NewServices();

        services.AddTraxOidcAuth<TestResolver>("https://id.example.com", "my-client");

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<OidcTokenInput>))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
    }

    [Test]
    public void Positional_EmptyAuthority_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxOidcAuth("", "my-client");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Positional_EmptyClientId_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxOidcAuth("https://id.example.com", "");

        act.Should().Throw<ArgumentException>();
    }

    private static async Task<IHost> CreateHost()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddTraxOidcAuth(b =>
                            b.UseAuthority("https://id.example.com", "my-client")
                                .AllowHttpMetadata()
                                .CustomizeOidcOptions(o =>
                                {
                                    // Short-circuit discovery: point at a fake config manager that
                                    // returns a minimal doc synchronously. Without this, a
                                    // challenge would try to fetch the real metadata and fail.
                                    o.ConfigurationManager = new FakeConfigManager();
                                })
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints
                                .MapGet("/protected", () => Results.Ok())
                                .RequireAuthorization(OidcDefaults.PolicyName);

                            endpoints.MapGet(
                                "/login",
                                (HttpContext ctx) =>
                                    Results.Challenge(
                                        new AuthenticationProperties { RedirectUri = "/done" },
                                        new[] { OidcDefaults.SchemeName }
                                    )
                            );
                        });
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    private sealed class FakeConfigManager
        : Microsoft.IdentityModel.Protocols.IConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>
    {
        public Task<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration> GetConfigurationAsync(
            CancellationToken cancel
        )
        {
            var config =
                new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
                {
                    Issuer = "https://id.example.com",
                    AuthorizationEndpoint = "https://id.example.com/authorize",
                    TokenEndpoint = "https://id.example.com/token",
                    EndSessionEndpoint = "https://id.example.com/logout",
                    JwksUri = "https://id.example.com/jwks",
                };
            return Task.FromResult(config);
        }

        public void RequestRefresh() { }
    }

    private sealed class TestResolver : ITraxPrincipalResolver<OidcTokenInput>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(OidcTokenInput input, CancellationToken ct) =>
            ValueTask.FromResult<TraxPrincipal?>(null);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
