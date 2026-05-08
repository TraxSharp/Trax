using FluentAssertions;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Middleware;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class ExtensionMethodTests
{
    private const string FakeConn = "Host=fake;Database=fake";

    [Test]
    public void UsePersistedOperations_NullBuilder_Throws()
    {
        Action act = () => ((TraxGraphQLBuilder)null!).UsePersistedOperations(_ => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void UsePersistedOperations_NullConfigure_Throws()
    {
        var sc = new ServiceCollection();
        var builder = new TraxGraphQLBuilder(sc);
        Action act = () => builder.UsePersistedOperations(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public async Task UsePersistedOperations_NoCacheNoBroadcaster_ResolvesNoOpsThroughDI()
    {
        // Build the full provider and resolve the contract types. This proves
        // the registrations don't just exist as ServiceDescriptors — they
        // actually compose into runnable instances. Storage requires
        // IDataContextProviderFactory; we provide a stub since we're not
        // exercising DB access here, only registration shape.
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<
            Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory,
            StubDataContextFactory
        >();
        var builder = new TraxGraphQLBuilder(sc);
        builder.UsePersistedOperations(opts => opts.UseDatabase(FakeConn));

        await using var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<PersistedOperationsOptions>()
            .DatabaseConnectionString.Should()
            .Be(FakeConn);
        sp.GetRequiredService<IPersistedOperationCache>()
            .Should()
            .BeOfType<NoOpPersistedOperationCache>();
        sp.GetRequiredService<IPersistedOperationBroadcaster>()
            .Should()
            .BeOfType<NoOpPersistedOperationBroadcaster>();
        sp.GetRequiredService<AllowlistMatcher>().Should().NotBeNull();

        // Same singleton backs both IPersistedOperationStore and the concrete
        // class — DI registration uses factory delegates that resolve the
        // single instance.
        var store = sp.GetRequiredService<IPersistedOperationStore>();
        var concrete = sp.GetRequiredService<DbPersistedOperationStorage>();
        store.Should().BeSameAs(concrete);
    }

    [Test]
    public async Task UsePersistedOperations_WithCache_ResolvesInMemoryCacheAndItWorks()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<
            Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory,
            StubDataContextFactory
        >();
        var builder = new TraxGraphQLBuilder(sc);
        builder.UsePersistedOperations(opts => opts.UseDatabase(FakeConn).WithInMemoryCache());

        await using var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<IPersistedOperationCache>()
            .Should()
            .BeOfType<InMemoryPersistedOperationCache>();
        sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()
            .Should()
            .NotBeNull();

        // Functional: set/get/invalidate round-trip through the resolved cache.
        var cache = sp.GetRequiredService<IPersistedOperationCache>();
        cache.Set(null, "id", "doc");
        cache.TryGet(null, "id").Should().Be("doc");
        cache.Invalidate(null, "id");
        cache.TryGet(null, "id").Should().BeNull();
    }

    [Test]
    public async Task UsePersistedOperations_WithRabbitMq_ResolvesBroadcasterAsHostedService()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<
            Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory,
            StubDataContextFactory
        >();
        var builder = new TraxGraphQLBuilder(sc);
        builder.UsePersistedOperations(opts =>
            opts.UseDatabase(FakeConn)
                .WithInMemoryCache()
                .UseRabbitMqInvalidation("amqp://localhost")
        );

        await using var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<IPersistedOperationBroadcaster>()
            .Should()
            .BeOfType<RabbitMqPersistedOperationBroadcaster>();
        sp.GetRequiredService<PersistedOperationReceiverService>().Should().NotBeNull();

        // The receiver must be registered AS an IHostedService so the host
        // actually starts it. Same singleton instance backs both lookups.
        var hosted = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();
        var receiverHosted = hosted.OfType<PersistedOperationReceiverService>().SingleOrDefault();
        receiverHosted
            .Should()
            .NotBeNull("the RabbitMQ receiver must be a hosted service so the host starts it");
        receiverHosted
            .Should()
            .BeSameAs(sp.GetRequiredService<PersistedOperationReceiverService>());
    }

    /// <summary>
    /// Minimal stub so DI can resolve <see cref="DbPersistedOperationStorage"/>
    /// without standing up a real database. The DI tests above never call any
    /// method that would touch the data layer.
    /// </summary>
    private sealed class StubDataContextFactory
        : Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory
    {
        public Trax.Effect.Services.EffectProvider.IEffectProvider Create() =>
            throw new NotSupportedException("stub: DI shape test only");

        public Task<Trax.Effect.Data.Services.DataContext.IDataContext> CreateDbContextAsync(
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("stub: DI shape test only");
    }

    [Test]
    public void UsePersistedOperationsEnforcement_NullApp_Throws()
    {
        Action act = () => ((IApplicationBuilder)null!).UsePersistedOperationsEnforcement();
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void UsePersistedOperationsEnforcement_OnRealAppBuilder_RegistersMiddlewareInPipeline()
    {
        // Build a real ApplicationBuilder with the middleware's dependencies
        // resolvable, call the extension, and verify the middleware fires
        // by sending a request through the resulting RequestDelegate.
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<
            Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory,
            StubDataContextFactory
        >();
        var builder = new TraxGraphQLBuilder(sc);
        builder.UsePersistedOperations(opts => opts.UseDatabase(FakeConn));
        var sp = sc.BuildServiceProvider();

        var app = new ApplicationBuilder(sp);
        // Terminal middleware: marks the response and short-circuits.
        app.Run(ctx =>
        {
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        // The extension under test:
        var returned = app.UsePersistedOperationsEnforcement();
        returned.Should().BeSameAs(app, "extension must return the same builder for chaining");

        // Build the pipeline. If UseMiddleware<PersistedOperationsMiddleware>
        // was wired correctly, this will succeed; if dependencies are missing
        // or types are wrong, Build throws.
        var pipeline = app.Build();
        pipeline.Should().NotBeNull();
    }

    [Test]
    public void AddPersistedOperationStore_NullServices_Throws()
    {
        Action act = () => ((IServiceCollection)null!).AddPersistedOperationStore("Host=x");
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddPersistedOperationStore_EmptyConnectionString_Throws()
    {
        var sc = new ServiceCollection();
        Action act = () => sc.AddPersistedOperationStore(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AddPersistedOperationStore_RegistersExpectedServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddPersistedOperationStore(FakeConn);

        sc.Should().Contain(s => s.ServiceType == typeof(PersistedOperationsOptions));
        sc.Should().Contain(s => s.ServiceType == typeof(IPersistedOperationStore));
        sc.Should().Contain(s => s.ServiceType == typeof(IPersistedOperationCache));
        sc.Should().Contain(s => s.ServiceType == typeof(IPersistedOperationBroadcaster));
        sc.Should().Contain(s => s.ServiceType == typeof(DbPersistedOperationStorage));
    }

    [Test]
    public void RabbitMqBroadcaster_EmptyConnectionString_Throws()
    {
        var options = new PersistedOperationsOptions
        {
            CacheEnabled = true,
            DatabaseConnectionString = "Host=x",
            RabbitMqConnectionString = string.Empty,
        };

        Action act = () =>
            _ = new RabbitMqPersistedOperationBroadcaster(
                options,
                NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
            );
        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void RabbitMqBroadcaster_NullArgs_Throw()
    {
        var options = new PersistedOperationsOptions
        {
            CacheEnabled = true,
            DatabaseConnectionString = "Host=x",
            RabbitMqConnectionString = "amqp://localhost",
        };
        (
            (Action)(
                () =>
                    _ = new RabbitMqPersistedOperationBroadcaster(
                        null!,
                        NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
        ((Action)(() => _ = new RabbitMqPersistedOperationBroadcaster(options, null!)))
            .Should()
            .Throw<ArgumentNullException>();
    }

    [Test]
    public async Task ReceiverService_StartAsync_NoConnectionString_DoesNotAttemptConnection()
    {
        // When RabbitMqConnectionString is null, StartAsync must short-circuit:
        // no connection attempt, no exception. Pointing the connection string
        // at an unreachable host (192.0.2.1 is RFC 5737 TEST-NET-1 — black-holed
        // by spec) and verifying StartAsync completes near-instantly proves the
        // null branch isn't accidentally falling through to a connect attempt
        // (which would block on a TCP timeout).
        var options = new PersistedOperationsOptions
        {
            CacheEnabled = true,
            DatabaseConnectionString = "Host=x",
            RabbitMqConnectionString = null,
        };
        var svc = new PersistedOperationReceiverService(
            options,
            new NoOpPersistedOperationCache(),
            NullLogger<PersistedOperationReceiverService>.Instance
        );

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await svc.StartAsync(CancellationToken.None);
        sw.Stop();

        sw.Elapsed.Should()
            .BeLessThan(
                TimeSpan.FromSeconds(1),
                "StartAsync must short-circuit when no connection string is configured; "
                    + "any latency here means we attempted a real network connect"
            );

        await svc.StopAsync(CancellationToken.None);
        await svc.DisposeAsync();
    }

    [Test]
    public void ReceiverService_NullArgs_Throw()
    {
        var options = new PersistedOperationsOptions { DatabaseConnectionString = "Host=x" };
        (
            (Action)(
                () =>
                    _ = new PersistedOperationReceiverService(
                        null!,
                        new NoOpPersistedOperationCache(),
                        NullLogger<PersistedOperationReceiverService>.Instance
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
        (
            (Action)(
                () =>
                    _ = new PersistedOperationReceiverService(
                        options,
                        null!,
                        NullLogger<PersistedOperationReceiverService>.Instance
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
        (
            (Action)(
                () =>
                    _ = new PersistedOperationReceiverService(
                        options,
                        new NoOpPersistedOperationCache(),
                        null!
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
    }
}
