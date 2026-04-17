using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class AddTraxJwtAuthTests
{
    private static readonly byte[] TestKey = Encoding.UTF8.GetBytes(new string('k', 32));

    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        return services;
    }

    private static void ConfigureDefault(JwtBuilder b) =>
        b.UseSymmetricKey("my-iss", "my-aud", TestKey);

    [Test]
    public void RegistersScheme()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = schemeProvider.GetSchemeAsync(JwtDefaults.SchemeName).GetAwaiter().GetResult();
        scheme.Should().NotBeNull();
    }

    [Test]
    public void RegistersDefaultResolver_AsSingleton()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(ConfigureDefault);

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Which.ImplementationType.Should()
            .Be(typeof(DefaultJwtPrincipalResolver));
    }

    [Test]
    public void WithCustomResolverType_RegistersScoped()
    {
        var services = NewServices();

        services.AddTraxJwtAuth<TestResolver>(ConfigureDefault);

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task RegistersJwtPolicy()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(JwtDefaults.PolicyName);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(JwtDefaults.SchemeName);
    }

    [Test]
    public async Task RegistersCombinedTraxAuthPolicy()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(TraxAuthClaimTypes.TraxAuthPolicy);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(JwtDefaults.SchemeName);
    }

    [Test]
    public void RegistersHttpContextAccessor()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(ConfigureDefault);

        services.Should().Contain(sd => sd.ServiceType == typeof(IHttpContextAccessor));
    }

    [Test]
    public void EmptyConfigure_ThrowsActionable()
    {
        var services = NewServices();

        var act = () => services.AddTraxJwtAuth(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseAuthority*UseSigningKey*");
    }

    [Test]
    public void NullConfigure_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxJwtAuth((Action<JwtBuilder>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public async Task EmitsStartupDisclaimer_Once()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(ConfigureDefault);
        using var sp = services.BuildServiceProvider();

        var hostedServices = sp.GetServices<IHostedService>()
            .Where(s => s.GetType().Name.Contains("Disclaimer"))
            .ToList();

        hostedServices.Should().HaveCount(1);
        await hostedServices[0].StartAsync(CancellationToken.None);
    }

    [Test]
    public void CalledTwice_RegistersDisclaimerOnce()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(ConfigureDefault);
        services.AddTraxJwtAuth(ConfigureDefault);

        var hostedCount = services.Count(sd =>
            sd.ServiceType == typeof(IHostedService)
            && sd.ImplementationType?.Name.Contains("Disclaimer") == true
        );

        hostedCount.Should().Be(1);
    }

    [Test]
    public void SigningKey_FlowsToTokenValidationParameters()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(b => b.UseSymmetricKey("iss-X", "aud-X", TestKey));
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtDefaults.SchemeName);

        options.TokenValidationParameters.ValidIssuer.Should().Be("iss-X");
        options.TokenValidationParameters.ValidAudience.Should().Be("aud-X");
        options
            .TokenValidationParameters.IssuerSigningKey.Should()
            .BeOfType<SymmetricSecurityKey>();
        options.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
        options.TokenValidationParameters.RequireSignedTokens.Should().BeTrue();
    }

    [Test]
    public void Authority_FlowsToOptions()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(b => b.UseAuthority("https://id.example.com", "my-aud"));
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtDefaults.SchemeName);

        options.Authority.Should().Be("https://id.example.com");
        options.Audience.Should().Be("my-aud");
        options.RequireHttpsMetadata.Should().BeTrue();
    }

    [Test]
    public void AllowHttpMetadata_FlowsToOptions()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(b =>
            b.UseAuthority("http://id.example.com", "my-aud").AllowHttpMetadata()
        );
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtDefaults.SchemeName);

        options.RequireHttpsMetadata.Should().BeFalse();
    }

    [Test]
    public void ClockSkew_FlowsToOptions()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(b =>
            b.UseSymmetricKey("iss", "aud", TestKey).WithClockSkew(TimeSpan.Zero)
        );
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtDefaults.SchemeName);

        options.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void CustomizeTokenValidation_Runs_AfterTraxDefaults()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(b =>
            b.UseSymmetricKey("iss", "aud", TestKey)
                .CustomizeTokenValidation(tvp => tvp.ValidAudiences = new[] { "aud", "aud-extra" })
        );
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtDefaults.SchemeName);

        options.TokenValidationParameters.ValidAudiences.Should().Contain("aud-extra");
    }

    [Test]
    public void Positional_WiresAuthorityAndAudience()
    {
        var services = NewServices();

        services.AddTraxJwtAuth("https://id.example.com", "my-aud");
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtDefaults.SchemeName);

        options.Authority.Should().Be("https://id.example.com");
        options.Audience.Should().Be("my-aud");
        options.RequireHttpsMetadata.Should().BeTrue();
    }

    [Test]
    public void Positional_UsesDefaultResolver()
    {
        var services = NewServices();

        services.AddTraxJwtAuth("https://id.example.com", "my-aud");

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Which.ImplementationType.Should()
            .Be(typeof(DefaultJwtPrincipalResolver));
    }

    [Test]
    public void Positional_WithResolverType_RegistersScoped()
    {
        var services = NewServices();

        services.AddTraxJwtAuth<TestResolver>("https://id.example.com", "my-aud");

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
    }

    [Test]
    public void Positional_EmptyAuthority_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxJwtAuth("", "my-aud");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Positional_EmptyAudience_Throws()
    {
        var services = NewServices();

        var act = () => services.AddTraxJwtAuth("https://id.example.com", "");

        act.Should().Throw<ArgumentException>();
    }

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
