using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class AddTraxApiKeyAuthTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        return services;
    }

    [Test]
    public void WithResolverType_RegistersScheme()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth<TestResolver>();
        using var sp = services.BuildServiceProvider();

        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = schemeProvider
            .GetSchemeAsync(ApiKeyDefaults.SchemeName)
            .GetAwaiter()
            .GetResult();
        scheme.Should().NotBeNull();
    }

    [Test]
    public void WithResolverType_RegistersResolverScoped()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth<TestResolver>();

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<string>))
            .Which.ImplementationType.Should()
            .Be(typeof(TestResolver));
    }

    [Test]
    public async Task WithResolverType_RegistersApiKeyPolicy()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth<TestResolver>();
        using var sp = services.BuildServiceProvider();

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(ApiKeyDefaults.PolicyName);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(ApiKeyDefaults.SchemeName);
    }

    [Test]
    public async Task WithResolverType_RegistersCombinedTraxAuthPolicy()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth<TestResolver>();
        using var sp = services.BuildServiceProvider();

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(TraxAuthClaimTypes.TraxAuthPolicy);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(ApiKeyDefaults.SchemeName);
    }

    [Test]
    public void WithBuilder_RegistersScheme()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth(keys => keys.Add("any-key", id: "any"));
        using var sp = services.BuildServiceProvider();

        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = schemeProvider
            .GetSchemeAsync(ApiKeyDefaults.SchemeName)
            .GetAwaiter()
            .GetResult();
        scheme.Should().NotBeNull();
    }

    [Test]
    public void WithBuilder_RegistersHashedResolverSingleton()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth(keys => keys.Add("any-key", id: "any"));

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<string>))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
    }

    [Test]
    public async Task WithBuilder_ResolvesRegisteredKey_ToConfiguredPrincipal()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth(keys => keys.Add("admin-key", id: "admin", "Admin", "Player"));
        using var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<ITraxPrincipalResolver<string>>();
        var result = await resolver.ResolveAsync("admin-key", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("admin");
        result.DisplayName.Should().Be("admin");
        result.Roles.Should().BeEquivalentTo("Admin", "Player");
        result.PrincipalType.Should().Be("apikey");
    }

    [Test]
    public async Task WithBuilder_UnknownKey_ResolvesToNull()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth(keys => keys.Add("known", id: "alice"));
        using var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<ITraxPrincipalResolver<string>>();
        var result = await resolver.ResolveAsync("unknown", CancellationToken.None);

        result.Should().BeNull();
    }

    [Test]
    public void WithBuilder_EmptyConfigure_ThrowsWithActionableMessage()
    {
        var services = NewServices();

        var act = () => services.AddTraxApiKeyAuth(_ => { });

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddTraxApiKeyAuth*at least one key*");
    }

    [Test]
    public void WithBuilder_NullConfigure_ThrowsArgumentNullException()
    {
        var services = NewServices();

        var act = () => services.AddTraxApiKeyAuth((Action<ApiKeyBuilder>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void RegistersHttpContextAccessor()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth<TestResolver>();

        services.Should().Contain(sd => sd.ServiceType == typeof(IHttpContextAccessor));
    }

    [Test]
    public async Task EmitsStartupDisclaimerLog_Once()
    {
        var services = NewServices();
        var logger = new RecordingLogger();
        services.AddSingleton<ILoggerFactory>(new RecordingLoggerFactory(logger));

        services.AddTraxApiKeyAuth<TestResolver>();
        using var sp = services.BuildServiceProvider();

        var hostedServices = sp.GetServices<IHostedService>()
            .Where(s => s.GetType().Name.Contains("Disclaimer"))
            .ToList();

        hostedServices.Should().HaveCount(1);
        await hostedServices[0].StartAsync(CancellationToken.None);

        logger
            .Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("NO WARRANTY"));
    }

    [Test]
    public void CalledTwice_StillRegistersDisclaimerServiceOnce()
    {
        var services = NewServices();

        services.AddTraxApiKeyAuth<TestResolver>();
        services.AddTraxApiKeyAuth<TestResolver>();

        var hostedCount = services.Count(sd =>
            sd.ServiceType == typeof(IHostedService)
            && sd.ImplementationType?.Name.Contains("Disclaimer") == true
        );

        hostedCount.Should().Be(1);
    }

    private sealed class TestResolver : ITraxPrincipalResolver<string>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct) =>
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

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed class RecordingLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose() { }
    }
}
