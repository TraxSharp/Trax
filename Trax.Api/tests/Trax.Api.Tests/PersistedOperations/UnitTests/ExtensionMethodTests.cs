using AwesomeAssertions;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Middleware;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Effect.Extensions;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[Property("adr", "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md")]
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
        builder.UsePersistedOperations(opts => opts.SingleNode());

        await using var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<PersistedOperationsOptions>().SingleNode.Should().BeTrue();
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
        builder.UsePersistedOperations(opts => opts.SingleNode().WithInMemoryCache());

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
            opts.WithInMemoryCache().UseRabbitMqInvalidation("amqp://localhost")
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
    public void UsePersistedOperationsEnforcement_AddsNothingToTheAspNetPipeline()
    {
        // Enforcement lives in HotChocolate's execution pipeline, so the ASP.NET extension is
        // kept for existing hosts and changes nothing. The terminal delegate still answers.
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        var returned = app.UsePersistedOperationsEnforcement();
        returned.Should().BeSameAs(app, "extension must return the same builder for chaining");
        app.Run(ctx =>
        {
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        app.Build()(context).GetAwaiter().GetResult();
        context.Response.StatusCode.Should().Be(204);
    }

    /// <summary>
    /// A store that can change what the GraphQL nodes serve refuses to start without a way to
    /// reach them. The overload that takes only a connection string has no way to say, so a
    /// host built against it is refused at startup.
    /// </summary>
    [Test]
    public void AddPersistedOperationStore_WithOnlyAConnectionString_RefusesToStart()
    {
        var overload = typeof(ServiceCollectionPersistedOperationsExtensions).GetMethod(
            nameof(ServiceCollectionPersistedOperationsExtensions.AddPersistedOperationStore),
            [typeof(IServiceCollection), typeof(string)]
        )!;

        var act = () => overload.Invoke(null, [new ServiceCollection(), FakeConn]);

        act.Should()
            .Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*UseRabbitMqInvalidation*SingleNode*");
    }

    /// <summary>
    /// The in-memory data provider has no transactions; the store still applies changes there,
    /// one process being all it serves.
    /// </summary>
    [Test]
    public async Task AStoreOverTheInMemoryProvider_AppliesChanges()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddTrax(trax =>
            trax.AddEffects(effects =>
                Trax.Effect.Data.InMemory.Extensions.ServiceExtensions.UseInMemory(effects)
            )
        );
        sc.AddPersistedOperationStore(store => store.SingleNode());
        await using var sp = sc.BuildServiceProvider();
        var store = sp.GetRequiredService<IPersistedOperationStore>();

        await store.UpsertAsync("mem_v1", "query Greet { hello }", null, CancellationToken.None);
        await store.DeactivateAsync("mem_v1", null, "retired", CancellationToken.None);
        await store.RestoreAsync("mem_v1", null, CancellationToken.None);

        (await store.GetAsync("mem_v1", null, CancellationToken.None)).Should().NotBeNull();
    }

    [Test]
    public void AddPersistedOperationStore_NullArguments_Throw()
    {
        Action nullServices = () =>
            ((IServiceCollection)null!).AddPersistedOperationStore(store => store.SingleNode());
        Action nullConfigure = () =>
            new ServiceCollection().AddPersistedOperationStore(
                (Action<PersistedOperationStoreBuilder>)null!
            );
        nullServices.Should().Throw<ArgumentNullException>();
        nullConfigure.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddPersistedOperationStore_NeitherABrokerNorSingleNode_RefusesToStart()
    {
        Action act = () => new ServiceCollection().AddPersistedOperationStore(_ => { });
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*UseRabbitMqInvalidation*SingleNode*");
    }

    [Test]
    public void AddPersistedOperationStore_BothABrokerAndSingleNode_RefusesToStart()
    {
        Action act = () =>
            new ServiceCollection().AddPersistedOperationStore(store =>
                store.SingleNode().UseRabbitMqInvalidation("amqp://localhost")
            );
        act.Should().Throw<InvalidOperationException>().WithMessage("*contradict*");
    }

    [Test]
    public void AddPersistedOperationStore_EmptyBrokerConnectionString_Throws()
    {
        Action act = () =>
            new ServiceCollection().AddPersistedOperationStore(store =>
                store.UseRabbitMqInvalidation(" ")
            );
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task AddPersistedOperationStore_WithABroker_BroadcastsOverRabbitMq()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddPersistedOperationStore(store => store.UseRabbitMqInvalidation("amqp://localhost"));
        await using var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<IPersistedOperationBroadcaster>()
            .Should()
            .BeOfType<RabbitMqPersistedOperationBroadcaster>();
        sp.GetRequiredService<PersistedOperationsOptions>()
            .RabbitMqConnectionString.Should()
            .Be("amqp://localhost");
    }

    [Test]
    public void AddPersistedOperationStore_SingleNode_BroadcastsNothing()
    {
        var sc = new ServiceCollection();
        sc.AddPersistedOperationStore(store => store.SingleNode());
        using var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<IPersistedOperationBroadcaster>()
            .Should()
            .BeOfType<NoOpPersistedOperationBroadcaster>();
    }

    /// <summary>
    /// The overload taking a database and a broker connection string still works for hosts built
    /// against it; the database string is not used. Called by reflection, as such a host would.
    /// </summary>
    [Test]
    public async Task AddPersistedOperationStore_TheTwoStringOverload_StillBroadcasts()
    {
        var overload = typeof(ServiceCollectionPersistedOperationsExtensions).GetMethod(
            nameof(ServiceCollectionPersistedOperationsExtensions.AddPersistedOperationStore),
            [typeof(IServiceCollection), typeof(string), typeof(string)]
        )!;
        var sc = new ServiceCollection();
        sc.AddLogging();

        overload.Invoke(null, [sc, FakeConn, "amqp://localhost"]);

        await using var sp = sc.BuildServiceProvider();
        sp.GetRequiredService<IPersistedOperationBroadcaster>()
            .Should()
            .BeOfType<RabbitMqPersistedOperationBroadcaster>();
    }

    [Test]
    public void AddPersistedOperationStore_RegistersExpectedServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddPersistedOperationStore(store => store.SingleNode());

        sc.Should().Contain(s => s.ServiceType == typeof(PersistedOperationsOptions));
        sc.Should().Contain(s => s.ServiceType == typeof(IPersistedOperationStore));
        sc.Should().Contain(s => s.ServiceType == typeof(IPersistedOperationCache));
        sc.Should().Contain(s => s.ServiceType == typeof(IPersistedOperationBroadcaster));
        sc.Should().Contain(s => s.ServiceType == typeof(DbPersistedOperationStorage));
    }

    /// <summary>
    /// A host with no GraphQL server, such as a CI uploader, calls only AddPersistedOperationStore.
    /// The store has to resolve from that container alone, with nothing the GraphQL path adds.
    /// </summary>
    [Test]
    public async Task AddPersistedOperationStore_Alone_ResolvesTheStore()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<
            Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory,
            StubDataContextFactory
        >();
        sc.AddPersistedOperationStore(store => store.SingleNode());

        await using var sp = sc.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );

        sp.GetRequiredService<IPersistedOperationStore>()
            .Should()
            .BeSameAs(sp.GetRequiredService<DbPersistedOperationStorage>());
    }

    /// <summary>
    /// With no request executor in the container, invalidating after a write has no
    /// HotChocolate cache to empty and must not fail.
    /// </summary>
    [Test]
    public async Task AddPersistedOperationStore_Alone_InvalidatorIsANoOp()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<
            Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory,
            StubDataContextFactory
        >();
        sc.AddPersistedOperationStore(store => store.SingleNode());
        await using var sp = sc.BuildServiceProvider();

        var invalidate = () =>
            sp.GetRequiredService<HotChocolateOperationCacheInvalidator>()
                .InvalidateAsync(CancellationToken.None);

        await invalidate.Should().NotThrowAsync();
    }

    [Test]
    public void RabbitMqBroadcaster_EmptyConnectionString_Throws()
    {
        var options = new PersistedOperationsOptions
        {
            CacheEnabled = true,
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
            RabbitMqConnectionString = null,
        };
        var svc = new PersistedOperationReceiverService(
            options,
            new NoOpPersistedOperationCache(),
            NoOpInvalidator(),
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
        var options = new PersistedOperationsOptions();
        (
            (Action)(
                () =>
                    _ = new PersistedOperationReceiverService(
                        null!,
                        new NoOpPersistedOperationCache(),
                        NoOpInvalidator(),
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
                        NoOpInvalidator(),
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
                        NoOpInvalidator(),
                        null!
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
    }

    private static HotChocolateOperationCacheInvalidator NoOpInvalidator() =>
        new(
            new ServiceCollection().BuildServiceProvider(),
            new PersistedOperationCacheGeneration(),
            NullLogger<HotChocolateOperationCacheInvalidator>.Instance
        );

    // ── ExposeOperationsNamespace ───────────────────────────────────────

    /// <summary>
    /// Persisted operations exposes the namespace by default, which is where its own management
    /// mutations live, and the host still has to answer for it.
    /// </summary>
    [Test]
    public void UsePersistedOperations_ByDefault_ExposesTheOperationsNamespace()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.UsePersistedOperations(po => po.SingleNode());

        var config = builder.AllowAnonymousOperations().Build();
        config.OperationQueriesExposed.Should().BeTrue();
        config.OperationMutationsExposed.Should().BeTrue();
        config.AdditionalTypeExtensions.Should().NotBeEmpty();
    }

    /// <summary>
    /// Declining it keeps enforcement and storage, and leaves the schema without a control plane.
    /// A host that manages its operations out of band should not have to publish one to get
    /// enforcement.
    /// </summary>
    [Test]
    public void ExposeOperationsNamespaceFalse_LeavesTheNamespaceOff()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.UsePersistedOperations(po => po.SingleNode().ExposeOperationsNamespace(false));

        var config = builder.Build();
        config
            .OperationQueriesExposed.Should()
            .BeFalse(
                "persisted operations and a GraphQL-exposed control plane are separable, per "
                    + "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md"
            );
        config.OperationMutationsExposed.Should().BeFalse();
        config
            .AdditionalTypeExtensions.Should()
            .BeEmpty(
                "the management type extensions target OperationsMutations, which is not in the "
                    + "schema when the namespace is declined"
            );
    }

    /// <summary>
    /// The enforcement middleware and storage are registered either way: the two features are
    /// separable, which is the point of the switch.
    /// </summary>
    [Test]
    public void ExposeOperationsNamespaceFalse_StillRegistersEnforcementAndStorage()
    {
        var sc = new ServiceCollection();
        var builder = new TraxGraphQLBuilder(sc);

        builder.UsePersistedOperations(po => po.SingleNode().ExposeOperationsNamespace(false));

        sc.Any(d => d.ServiceType == typeof(IPersistedOperationStore)).Should().BeTrue();
        sc.Any(d => d.ServiceType == typeof(PersistedOperationsOptions)).Should().BeTrue();
    }

    /// <summary>
    /// Without the namespace there is nothing to acknowledge, so a host that declines it does not
    /// have to call AllowAnonymousOperations() or gate anything.
    /// </summary>
    [Test]
    public void ExposeOperationsNamespaceFalse_NeedsNoAcknowledgement()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.UsePersistedOperations(po => po.SingleNode().ExposeOperationsNamespace(false));

        Action act = () => builder.Build();

        act.Should().NotThrow();
    }

    [Test]
    public void ExposeOperationsNamespaceTrue_IsTheDefaultAndStillRequiresAnAnswer()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.UsePersistedOperations(po => po.SingleNode());

        Action act = () => builder.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*GateOperations(policy, roles)*");
    }

    [Test]
    public void ExposeOperationsNamespace_ReturnsSameBuilder()
    {
        var po = new PersistedOperationsBuilder();

        po.ExposeOperationsNamespace(false).Should().BeSameAs(po);
    }
}
