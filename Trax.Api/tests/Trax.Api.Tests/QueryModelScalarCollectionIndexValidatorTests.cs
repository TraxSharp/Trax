using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Coverage for the startup warning about scalar collections with no GIN index declared
/// in the EF model.
/// </summary>
/// <remarks>
/// The declaration decides which operator Npgsql compiles for a single-value membership
/// filter: <c>col @> ARRAY[@p]</c> when an index is declared, <c>@p = ANY(col)</c> when
/// it is not. Both return the same rows and produce the same GraphQL schema, so the
/// difference is invisible until it shows up as a sequential scan under load.
/// </remarks>
[TestFixture]
public class QueryModelScalarCollectionIndexValidatorTests
{
    [Test]
    public async Task UnindexedScalarCollection_LogsWarningNamingThePropertyAndFix()
    {
        var warnings = await RunValidatorAsync<IndexProbeDbContext>();

        var warning = warnings.Should().ContainSingle(w => w.Contains("Unindexed")).Subject;

        // The message has to be actionable on its own: which property, and what to do.
        warning.Should().Contain("Loose");
        warning.Should().Contain("gin");
        warning.Should().Contain("= ANY");
    }

    [Test]
    public async Task IgnoredScalarCollection_LogsNothing()
    {
        // The filter input is the source of truth for what a caller can filter on. When a
        // registered AddFilterType override removes a scalar collection from the input,
        // there is no `some: { eq: }` filter left for it to slow down, so the
        // missing-index warning is wrong for it and must not fire.
        var warnings = await RunValidatorAsync<IndexProbeDbContext>(b =>
            b.AddFilterType<UnindexedRow, UnindexedRowFilterInputType>()
        );

        warnings.Should().NotContain(w => w.Contains("Loose"));
    }

    [Test]
    public async Task IndexedScalarCollection_LogsNothing()
    {
        var warnings = await RunValidatorAsync<IndexProbeDbContext>();

        // Two warnings across the whole model: the un-indexed collection and the
        // btree-indexed one. If the GIN-indexed collection also warned, this would be
        // three, and if the scalar or navigation properties warned, more still.
        warnings.Should().HaveCount(2);
        warnings.Should().Contain(w => w.Contains("Loose"));
    }

    [Test]
    public async Task NonGinIndexedScalarCollection_StillLogsWarning()
    {
        // A plain HasIndex(...) is a btree. It cannot serve `@>` any more than no index
        // can, and it does not make Npgsql emit `@>` either, so it still needs saying.
        var warnings = await RunValidatorAsync<IndexProbeDbContext>();

        warnings.Should().Contain(w => w.Contains("Btree"));
    }

    [Test]
    public async Task UnresolvableDbContext_DoesNotThrow()
    {
        // A query model whose DbContext is not in DI is a host misconfiguration that
        // other startup paths report. This validator is advisory and must not be what
        // takes the host down.
        var recorder = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(recorder).SetMinimumLevel(LogLevel.Warning));

        var builder = new TraxGraphQLBuilder(services);
        builder.AddDbContext<IndexProbeDbContext>();
        var config = builder.Build();
        services.AddSingleton(config);

        await using var provider = services.BuildServiceProvider();

        var validatorType = typeof(GraphQLConfiguration).Assembly.GetType(
            "Trax.Api.GraphQL.Startup.QueryModelScalarCollectionIndexValidator"
        )!;
        var validator = (IHostedService)ActivatorUtilities.CreateInstance(provider, validatorType);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        recorder.Messages.Should().BeEmpty();
    }

    [Test]
    public async Task NavigationCollection_LogsNothing()
    {
        // A join has no array column and no GIN index to miss.
        var warnings = await RunValidatorAsync<IndexProbeDbContext>();

        warnings.Should().NotContain(w => w.Contains("Chapters"));
    }

    [Test]
    public async Task ScalarProperty_LogsNothing()
    {
        var warnings = await RunValidatorAsync<IndexProbeDbContext>();

        warnings.Should().NotContain(w => w.Contains("Title"));
    }

    [Test]
    public async Task NonPostgresProvider_LogsNothing()
    {
        // The operator choice is Npgsql behaviour; other providers have no GIN to miss.
        var warnings = await RunValidatorAsync<InMemoryProbeDbContext>();

        warnings.Should().BeEmpty();
    }

    #region Harness

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task ThroughTheRealWiring_AnExposedCollectionIsReported_AndAnIgnoredOneIsNot(
        bool ignored,
        bool reported
    )
    {
        // AddTraxGraphQL builds the schema the validator reads, so a change in how it names the
        // discover field or the where argument shows up here, where the hand-built harness above
        // would still pass.
        var recorder = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(recorder).SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<IndexProbeDbContext>();
        services.AddTraxGraphQL(g =>
        {
            g.AddDbContext<IndexProbeDbContext>();
            if (ignored)
                g.AddFilterType<UnindexedRow, UnindexedRowFilterInputType>();
            return g;
        });

        await using var provider = services.BuildServiceProvider();
        var validator = provider
            .GetServices<IHostedService>()
            .Single(h => h.GetType().Name == "QueryModelScalarCollectionIndexValidator");
        await validator.StartAsync(CancellationToken.None);

        recorder
            .Messages.Any(w => w.Contains("Loose"))
            .Should()
            .Be(
                reported,
                ignored
                    ? "the filter input has no Loose field"
                    : "Loose is filterable and has no GIN index"
            );
    }

    private static async Task<List<string>> RunValidatorAsync<TContext>(
        Action<TraxGraphQLBuilder>? customize = null
    )
        where TContext : DbContext
    {
        var recorder = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(recorder).SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<TContext>();

        var builder = new TraxGraphQLBuilder(services);
        builder.AddDbContext<TContext>();
        customize?.Invoke(builder);
        var config = builder.Build();
        services.AddSingleton(config);

        // The validator checks the EF model against what the built schema exposes, so the
        // harness registers a minimal "trax" server the way the real wiring does: a root
        // query with `discover`, the query-model fields, and the stock conventions that
        // build the `where` argument.
        services.AddSingleton<QueryModelTypeModule>();
        services
            .AddGraphQLServer("trax")
            .AddQueryType<ValidatorRootQuery>()
            .AddType(new ObjectType<DiscoverQueries>())
            .AddTypeModule<QueryModelTypeModule>()
            .AddFiltering()
            .AddSorting()
            .AddProjections();

        services.AddHostedService<QueryModelScalarCollectionIndexValidatorAccessor<TContext>>();

        await using var provider = services.BuildServiceProvider();

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        return recorder.Messages;
    }

    /// <summary>
    /// The validator is internal to Trax.Api.GraphQL and constructed by the real wiring.
    /// This shim resolves it the same way the host would so the test drives the actual
    /// type rather than a copy of its logic.
    /// </summary>
    private sealed class QueryModelScalarCollectionIndexValidatorAccessor<TContext>(
        IServiceProvider serviceProvider
    ) : IHostedService
        where TContext : DbContext
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var validatorType = typeof(GraphQLConfiguration).Assembly.GetType(
                "Trax.Api.GraphQL.Startup.QueryModelScalarCollectionIndexValidator"
            )!;

            var validator = (IHostedService)
                ActivatorUtilities.CreateInstance(serviceProvider, validatorType);

            return validator.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// The minimal RootQuery of the harness server: a <c>discover</c> field resolving the
    /// base type the query-model fields extend, the way the real wiring does.
    /// </summary>
    public class ValidatorRootQuery
    {
        public DiscoverQueries Discover() => new();
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose() { }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (IsEnabled(logLevel))
                    lock (messages)
                        messages.Add(formatter(state, exception));
            }
        }
    }

    #endregion
}

#region Probe entities and contexts

[TraxAllowAnonymous]
[TraxQueryModel(Name = "indexedRows")]
public class IndexedRow
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string[] Tight { get; set; } = [];
    public string[] Btree { get; set; } = [];
    public List<ChapterRow> Chapters { get; set; } = [];
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "unindexedRows")]
public class UnindexedRow
{
    public int Id { get; set; }
    public string[] Loose { get; set; } = [];
}

public class UnindexedRowFilterInputType : FilterInputType<UnindexedRow>
{
    protected override void Configure(IFilterInputTypeDescriptor<UnindexedRow> descriptor)
    {
        descriptor.Ignore(x => x.Loose);
    }
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "chapterRows")]
public class ChapterRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class IndexProbeDbContext : DbContext
{
    public DbSet<IndexedRow> Indexed => Set<IndexedRow>();
    public DbSet<UnindexedRow> Unindexed => Set<UnindexedRow>();
    public DbSet<ChapterRow> Chapters => Set<ChapterRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseNpgsql("Host=localhost;Database=probe;Username=probe;Password=probe");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IndexedRow>().HasIndex(r => r.Tight).HasMethod("gin");
        // Indexed, but not with a method that can serve array containment.
        modelBuilder.Entity<IndexedRow>().HasIndex(r => r.Btree);
    }
}

public class InMemoryProbeDbContext : DbContext
{
    public DbSet<UnindexedRow> Unindexed => Set<UnindexedRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseInMemoryDatabase("IndexProbe_" + Guid.NewGuid());
}

#endregion
