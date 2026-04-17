using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class JwtProviderExtensionsTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        return services;
    }

    private static JwtBearerOptions GetOptions(IServiceProvider sp) =>
        sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtDefaults.SchemeName);

    // ── Google ───────────────────────────────────────────────────────────

    [Test]
    public void Google_BakesAuthorityAndUsesClientIdAsAudience()
    {
        var services = NewServices();

        services.AddTraxGoogleJwtAuth("my-google-client");
        using var sp = services.BuildServiceProvider();

        var opts = GetOptions(sp);
        opts.Authority.Should().Be("https://accounts.google.com");
        opts.Audience.Should().Be("my-google-client");
    }

    [Test]
    public void Google_UsesDefaultResolver()
    {
        var services = NewServices();

        services.AddTraxGoogleJwtAuth("cid");

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Which.ImplementationType.Should()
            .Be(typeof(DefaultJwtPrincipalResolver));
    }

    [Test]
    public void Google_WithResolverType_RegistersScoped()
    {
        var services = NewServices();

        services.AddTraxGoogleJwtAuth<TestResolver>("cid");

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
    }

    [Test]
    public void Google_EmptyClientId_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxGoogleJwtAuth("");

        act.Should().Throw<ArgumentException>();
    }

    // ── Auth0 ────────────────────────────────────────────────────────────

    [Test]
    public void Auth0_BakesAuthorityFromBareDomain()
    {
        var services = NewServices();

        services.AddTraxAuth0JwtAuth("my-tenant.auth0.com", "https://api.example.com");
        using var sp = services.BuildServiceProvider();

        var opts = GetOptions(sp);
        opts.Authority.Should().Be("https://my-tenant.auth0.com/");
        opts.Audience.Should().Be("https://api.example.com");
    }

    [Test]
    public void Auth0_NormalizesDomainWithSchemePrefix()
    {
        JwtProviderExtensions
            .BuildAuth0Authority("https://my-tenant.auth0.com")
            .Should()
            .Be("https://my-tenant.auth0.com/");
    }

    [Test]
    public void Auth0_NormalizesDomainWithHttpPrefix()
    {
        JwtProviderExtensions
            .BuildAuth0Authority("http://my-tenant.auth0.com")
            .Should()
            .Be("https://my-tenant.auth0.com/");
    }

    [Test]
    public void Auth0_NormalizesDomainWithTrailingSlash()
    {
        JwtProviderExtensions
            .BuildAuth0Authority("my-tenant.auth0.com/")
            .Should()
            .Be("https://my-tenant.auth0.com/");
    }

    [Test]
    public void Auth0_TrimsWhitespace()
    {
        JwtProviderExtensions
            .BuildAuth0Authority("  my-tenant.auth0.com  ")
            .Should()
            .Be("https://my-tenant.auth0.com/");
    }

    [Test]
    public void Auth0_CustomDomain_Works()
    {
        JwtProviderExtensions
            .BuildAuth0Authority("auth.example.com")
            .Should()
            .Be("https://auth.example.com/");
    }

    [Test]
    public void Auth0_EmptyDomain_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxAuth0JwtAuth("", "aud");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Auth0_WithResolverType_WiresBoth()
    {
        var services = NewServices();

        services.AddTraxAuth0JwtAuth<TestResolver>("tenant.auth0.com", "api");
        using var sp = services.BuildServiceProvider();

        GetOptions(sp).Authority.Should().Be("https://tenant.auth0.com/");
        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>)
                && sd.ImplementationType == typeof(TestResolver)
            );
    }

    // ── Microsoft Entra ID ───────────────────────────────────────────────

    [Test]
    public void Entra_BakesV2AuthorityForTenantGuid()
    {
        var services = NewServices();
        var tenantId = "11111111-2222-3333-4444-555555555555";

        services.AddTraxEntraJwtAuth(tenantId, "api://my-app");
        using var sp = services.BuildServiceProvider();

        var opts = GetOptions(sp);
        opts.Authority.Should().Be($"https://login.microsoftonline.com/{tenantId}/v2.0");
        opts.Audience.Should().Be("api://my-app");
    }

    [Test]
    public void Entra_BakesV2AuthorityForVerifiedDomain()
    {
        JwtProviderExtensions
            .BuildEntraAuthority("contoso.onmicrosoft.com")
            .Should()
            .Be("https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0");
    }

    [Test]
    public void Entra_BakesCommonEndpoint()
    {
        JwtProviderExtensions
            .BuildEntraAuthority("common")
            .Should()
            .Be("https://login.microsoftonline.com/common/v2.0");
    }

    [Test]
    public void Entra_EmptyTenantId_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxEntraJwtAuth("", "aud");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Entra_WithResolverType_WiresBoth()
    {
        var services = NewServices();

        services.AddTraxEntraJwtAuth<TestResolver>("tenantId", "audience");

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>)
                && sd.ImplementationType == typeof(TestResolver)
            );
    }

    // ── Cognito ──────────────────────────────────────────────────────────

    [Test]
    public void Cognito_BakesRegionAndUserPoolIntoAuthority()
    {
        var services = NewServices();

        services.AddTraxCognitoJwtAuth("us-east-1", "us-east-1_AbCdEfGhI", "app-client-id");
        using var sp = services.BuildServiceProvider();

        var opts = GetOptions(sp);
        opts.Authority.Should()
            .Be("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_AbCdEfGhI");
        opts.Audience.Should().Be("app-client-id");
    }

    [Test]
    public void Cognito_EuRegion_Works()
    {
        JwtProviderExtensions
            .BuildCognitoAuthority("eu-west-2", "eu-west-2_XyZ")
            .Should()
            .Be("https://cognito-idp.eu-west-2.amazonaws.com/eu-west-2_XyZ");
    }

    [Test]
    public void Cognito_EmptyRegion_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxCognitoJwtAuth("", "pool", "aud");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Cognito_EmptyUserPoolId_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxCognitoJwtAuth("us-east-1", "", "aud");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Cognito_WithResolverType_WiresBoth()
    {
        var services = NewServices();

        services.AddTraxCognitoJwtAuth<TestResolver>("us-east-1", "pool", "aud");

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>)
                && sd.ImplementationType == typeof(TestResolver)
            );
    }

    // ── Cross-provider: combined TraxAuthPolicy still includes the scheme ──

    [Test]
    public async Task ProviderShortcut_RegistersScheme_UnderStandardName()
    {
        var services = NewServices();

        services.AddTraxGoogleJwtAuth("cid");
        using var sp = services.BuildServiceProvider();

        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemes.GetSchemeAsync(JwtDefaults.SchemeName);

        scheme.Should().NotBeNull();
    }

    [Test]
    public void ProviderShortcut_RegistersHttpContextAccessor()
    {
        var services = NewServices();

        services.AddTraxEntraJwtAuth("tenantId", "audience");

        services.Should().Contain(sd => sd.ServiceType == typeof(IHttpContextAccessor));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private sealed class TestResolver : ITraxPrincipalResolver<JwtTokenInput>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct) =>
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
