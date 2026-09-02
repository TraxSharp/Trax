using FluentAssertions;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

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
    public async Task AddAudit_ThroughAddTraxGraphQL_AuditsAnExecutedQuery()
    {
        // The schema configuration AddAudit registers is only proof of anything once it runs.
        // Going through AddTraxGraphQL executes it, which is what wires the diagnostic listener
        // and bridges the services it is constructed from into the schema container. Asserting
        // the callback merely exists would pass even if every one of those bridges were dropped.
        var services = AuditHostServices();
        services.AddTraxGraphQL(graphql =>
            graphql.AddDbContext<AuditWiringDbContext>().AddAudit<CapturingSink>()
        );

        var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var result = await executor.ExecuteAsync(
            "{ discover { auditWiringWidgets { totalCount } } }"
        );

        result.ExpectOperationResult().Errors.Should().BeNullOrEmpty();

        var channel = provider.GetRequiredService<TraxAuditChannel>();
        channel.Reader.TryRead(out var entry).Should().BeTrue("the listener should have run");
        entry!.Document.Should().Contain("auditWiringWidgets");
    }

    private sealed class CapturingSink : ITraxAuditSink
    {
        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private static ServiceCollection AuditHostServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<AuditWiringDbContext>(o =>
            o.UseInMemoryDatabase("AuditWiring_" + Guid.NewGuid())
        );
        return services;
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

public class AuditWiringDbContext(DbContextOptions<AuditWiringDbContext> options)
    : DbContext(options)
{
    public DbSet<AuditWiringWidget> Widgets => Set<AuditWiringWidget>();
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "auditWiringWidgets")]
public class AuditWiringWidget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
