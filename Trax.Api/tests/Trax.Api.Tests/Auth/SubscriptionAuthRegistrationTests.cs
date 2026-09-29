using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Verifies the registrations <see cref="Trax.Api.GraphQL.Subscriptions.TraxCompositeSocketInterceptor"/>
/// reads to decide which strategies are active: the per-scheme resolver each auth extension
/// registers. Routing between them is covered by <see cref="TraxCompositeSocketInterceptorTests"/>,
/// and each strategy by <see cref="TraxJwtSocketInterceptorTests"/> and
/// <see cref="TraxApiKeySocketInterceptorTests"/>.
/// </summary>
[TestFixture]
public class SubscriptionAuthRegistrationTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        return services;
    }

    [Test]
    public void NoAuthRegistered_NoResolversPresent()
    {
        var services = NewServices();

        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<string>))
            .Should()
            .BeFalse();
        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Should()
            .BeFalse();
    }

    [Test]
    public void ApiKeyAuthRegistered_StringResolverPresent()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth(keys => keys.Add("k", id: "alice"));

        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<string>))
            .Should()
            .BeTrue();
        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Should()
            .BeFalse();
    }

    [Test]
    public void JwtAuthRegistered_JwtTokenInputResolverPresent()
    {
        var services = NewServices();
        var key = Encoding.UTF8.GetBytes(new string('k', 32));

        services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey("iss", "aud", key));

        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Should()
            .BeTrue();
        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<string>))
            .Should()
            .BeFalse();
    }

    [Test]
    public void BothAuthSchemes_BothResolversPresent()
    {
        var services = NewServices();
        var key = Encoding.UTF8.GetBytes(new string('k', 32));

        services.AddTraxApiKeyAuth(keys => keys.Add("k", id: "alice"));
        services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey("iss", "aud", key));

        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<string>))
            .Should()
            .BeTrue();
        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Should()
            .BeTrue();
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
