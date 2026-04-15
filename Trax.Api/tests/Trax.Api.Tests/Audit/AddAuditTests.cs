using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

namespace Trax.Api.Tests.Audit;

[TestFixture]
public class AddAuditTests
{
    private sealed class TestSink : ITraxAuditSink
    {
        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private static (ServiceCollection services, TraxGraphQLBuilder builder) NewBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TraxGraphQLBuilder(services);
        return (services, builder);
    }

    [Test]
    public void AddAudit_RegistersChannelAsSingleton()
    {
        var (services, builder) = NewBuilder();

        builder.AddAudit<TestSink>();

        services
            .Should()
            .ContainSingle(sd =>
                sd.ServiceType == typeof(TraxAuditChannel)
                && sd.Lifetime == ServiceLifetime.Singleton
            );
    }

    [Test]
    public void AddAudit_RegistersWriterAsHostedService()
    {
        var (services, builder) = NewBuilder();

        builder.AddAudit<TestSink>();

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(IHostedService)
                && sd.ImplementationType == typeof(TraxAuditWriter)
            );
    }

    [Test]
    public void AddAudit_RegistersSink()
    {
        var (services, builder) = NewBuilder();

        builder.AddAudit<TestSink>();

        services.Should().Contain(sd => sd.ServiceType == typeof(ITraxAuditSink));
    }

    [Test]
    public void AddAudit_HonorsOptionsOverrides()
    {
        var (services, builder) = NewBuilder();

        builder.AddAudit<TestSink>(opts =>
        {
            opts.BatchSize = 99;
            opts.DefaultPrincipalId = "ghost";
        });
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<TraxAuditOptions>>().Value;

        opts.BatchSize.Should().Be(99);
        opts.DefaultPrincipalId.Should().Be("ghost");
    }

    [Test]
    public void AddAudit_WithoutCustomRedactor_UsesDefaultPassthrough()
    {
        var (services, builder) = NewBuilder();

        builder.AddAudit<TestSink>();
        using var sp = services.BuildServiceProvider();

        var redactor = sp.GetRequiredService<ITraxAuditRedactor>();

        redactor.Should().BeOfType<DefaultAuditRedactor>();
    }

    [Test]
    public void AddAudit_WithCustomRedactor_UsesHostImplementation()
    {
        var (services, builder) = NewBuilder();
        services.AddSingleton<ITraxAuditRedactor, DroppingRedactor>();

        builder.AddAudit<TestSink>();
        using var sp = services.BuildServiceProvider();

        var redactor = sp.GetRequiredService<ITraxAuditRedactor>();

        redactor.Should().BeOfType<DroppingRedactor>();
    }

    [Test]
    public void AddAudit_RegistersListenerSchemaConfiguration()
    {
        var (_, builder) = NewBuilder();

        builder.AddAudit<TestSink>();

        var config = builder.Build();
        config.SchemaConfigurations.Should().NotBeEmpty();
    }

    [Test]
    public void AddAudit_CalledTwice_StillRegistersDisclaimerOnce()
    {
        var (services, builder) = NewBuilder();

        builder.AddAudit<TestSink>();
        builder.AddAudit<TestSink>();

        var disclaimerCount = services.Count(sd =>
            sd.ServiceType == typeof(IHostedService)
            && sd.ImplementationType?.Name.Contains("Disclaimer") == true
        );
        disclaimerCount.Should().Be(1);
    }

    private sealed class DroppingRedactor : ITraxAuditRedactor
    {
        public IReadOnlyDictionary<string, object?>? Redact(
            IReadOnlyDictionary<string, object?>? variables
        ) => null;
    }
}
