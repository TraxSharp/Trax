using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.Fakes;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

[TestFixture]
public class LogQueriesTests
{
    // Pool tuning matches the AuthE2E hardening from PR #41 after the same
    // class of CI flake here: aggressive Connection Pruning Interval=1 +
    // Idle Lifetime=1 forced every SetUp to pay TCP+auth and timed out under
    // contention. Pool Size=8 across the four test fixtures in this assembly
    // stays well under Postgres's default max_connections=100.
    private static readonly string ConnectionString =
        $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_logs;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=30;"
        + "Timeout=30;Tcp Keepalive=true";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;

    private IOperationsService Operations =>
        new OperationsService(
            Substitute.For<ITrainDiscoveryService>(),
            _factory,
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>()
        );

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t => t.AddEffects(e => e.UsePostgres(ConnectionString)));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var ctx = (DbContext)db;
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE trax.log, trax.metadata, trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );
    }

    private async Task<long> SeedMetadata()
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.Tests.IFakeTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await db.Track(meta);
        await db.SaveChanges(default);
        return meta.Id;
    }

    private async Task SeedLogs(
        long metadataId,
        int count,
        LogLevel level = LogLevel.Information,
        string category = "Test"
    )
    {
        // MetadataId on Log has a private setter, so we insert via raw SQL. This matches
        // how production code populates the table (the framework writes logs through a
        // logger provider that sets metadata_id directly).
        await using var db = await _factory.CreateDbContextAsync(default);
        var ctx = (DbContext)db;
        // Postgres has a custom enum type for log_level; cast the string explicitly.
        var levelName = level.ToString().ToLowerInvariant();
        for (var i = 0; i < count; i++)
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO trax.log (metadata_id, event_id, level, message, category) VALUES ({metadataId}, {i}, {levelName}::trax.log_level, {$"msg-{i}"}, {category})"
            );
        }
    }

    [Test]
    public async Task GetLogs_NoData_ReturnsEmpty()
    {
        var queries = new LogQueries();

        var result = await queries.GetLogs(Operations, _factory, default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.NextCursor.Should().BeNull();
    }

    [Test]
    public async Task GetLogs_PaginatesAndExposesCursor()
    {
        var metaId = await SeedMetadata();
        await SeedLogs(metaId, 5);
        var queries = new LogQueries();

        var result = await queries.GetLogs(Operations, _factory, default, take: 2);

        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();
    }

    [Test]
    public async Task GetLogs_AfterIdCursor_FiltersByCursorAndUsesExactCount()
    {
        var metaId = await SeedMetadata();
        await SeedLogs(metaId, 5);
        var queries = new LogQueries();
        var first = await queries.GetLogs(Operations, _factory, default, take: 2);

        var page2 = await queries.GetLogs(
            Operations,
            _factory,
            default,
            take: 2,
            afterId: first.NextCursor
        );

        page2.Items.Should().HaveCount(2);
        page2
            .Items.Select(l => l.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(first.NextCursor!.Value));
        page2.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetLogs_SkipPagination_HonorsSkip()
    {
        var metaId = await SeedMetadata();
        await SeedLogs(metaId, 5);
        var queries = new LogQueries();

        var result = await queries.GetLogs(Operations, _factory, default, skip: 2, take: 2);

        result.Items.Should().HaveCount(2);
        result.Skip.Should().Be(2);
    }

    [Test]
    public async Task GetLogs_MetadataIdFilter_OnlyMatching()
    {
        var meta1 = await SeedMetadata();
        var meta2 = await SeedMetadata();
        await SeedLogs(meta1, 2);
        await SeedLogs(meta2, 3);
        var queries = new LogQueries();

        var meta1Logs = await queries.GetLogs(Operations, _factory, default, metadataId: meta1);
        var meta2Logs = await queries.GetLogs(Operations, _factory, default, metadataId: meta2);

        meta1Logs.Items.Should().HaveCount(2);
        meta1Logs.Items.Should().OnlyContain(l => l.MetadataId == meta1);
        meta2Logs.Items.Should().HaveCount(3);
    }

    [Test]
    public async Task GetLogs_MinimumLevelFilter_IncludesEqualOrAbove()
    {
        var metaId = await SeedMetadata();
        await SeedLogs(metaId, 2, LogLevel.Debug);
        await SeedLogs(metaId, 3, LogLevel.Warning);
        await SeedLogs(metaId, 1, LogLevel.Error);
        var queries = new LogQueries();

        var warningOrAbove = await queries.GetLogs(
            Operations,
            _factory,
            default,
            minimumLevel: LogLevel.Warning
        );

        warningOrAbove.Items.Should().HaveCount(4);
        warningOrAbove.Items.Should().OnlyContain(l => l.Level >= LogLevel.Warning);
    }

    [Test]
    public async Task GetLogs_CategoryFilter_ExactMatch()
    {
        var metaId = await SeedMetadata();
        await SeedLogs(metaId, 2, category: "Alpha");
        await SeedLogs(metaId, 3, category: "Beta");
        var queries = new LogQueries();

        var alpha = await queries.GetLogs(Operations, _factory, default, category: "Alpha");

        alpha.Items.Should().HaveCount(2);
        alpha.Items.Should().OnlyContain(l => l.Category == "Alpha");
    }

    [Test]
    public async Task GetLogs_WhitespaceCategory_TreatedAsNoFilter()
    {
        var metaId = await SeedMetadata();
        await SeedLogs(metaId, 3);
        var queries = new LogQueries();

        var result = await queries.GetLogs(Operations, _factory, default, category: "   ");

        result.Items.Should().HaveCount(3);
    }

    [Test]
    public async Task GetLogs_AllFieldsPopulatedFromRow()
    {
        var metaId = await SeedMetadata();
        await SeedLogs(metaId, 1, LogLevel.Error, "MyCat");
        var queries = new LogQueries();

        var result = await queries.GetLogs(Operations, _factory, default);

        var entry = result.Items.Single();
        entry.MetadataId.Should().Be(metaId);
        entry.EventId.Should().Be(0);
        entry.Level.Should().Be(LogLevel.Error);
        entry.Category.Should().Be("MyCat");
        entry.Message.Should().Be("msg-0");
        entry.Exception.Should().BeNull();
        entry.StackTrace.Should().BeNull();
    }

    // Above CountEstimator's threshold, so the database's estimate is used when it has one.
    private const int LargeTableRows = 12_000;

    private async Task SeedLargeAnalyzedLogTable(long metadataId)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var ctx = (DbContext)db;
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO trax.log (metadata_id, event_id, level, message, category) SELECT {metadataId}, g, 'information'::trax.log_level, 'msg', 'Test' FROM generate_series(1, {LargeTableRows}) g"
        );
        // reltuples is what ANALYZE (or autovacuum) last measured.
        await ctx.Database.ExecuteSqlRawAsync("ANALYZE trax.log");
    }

    [Test]
    public async Task GetLogs_UnfilteredTotalOfALargeTable_IsTheDatabasesEstimateOnEveryPage()
    {
        var metaId = await SeedMetadata();
        await SeedLargeAnalyzedLogTable(metaId);
        var dialect = _provider.GetRequiredService<ISqlDialect>();
        var queries = new LogQueries();

        var first = await queries.GetLogs(
            Operations,
            _factory,
            default,
            take: 10,
            sqlDialect: dialect
        );
        var second = await queries.GetLogs(
            Operations,
            _factory,
            default,
            take: 10,
            afterId: first.NextCursor,
            sqlDialect: dialect
        );

        first.IsEstimatedCount.Should().BeTrue("an unfiltered total of a large table is estimated");
        first.TotalCount.Should().BeCloseTo(LargeTableRows, 500);
        second
            .IsEstimatedCount.Should()
            .BeTrue("the cursor does not change how the whole list is counted");
        second.TotalCount.Should().Be(first.TotalCount, "totalCount does not depend on the page");
    }

    [Test]
    public async Task GetLogs_FilteredTotalOfALargeTable_IsExact()
    {
        var metaId = await SeedMetadata();
        await SeedLargeAnalyzedLogTable(metaId);
        var dialect = _provider.GetRequiredService<ISqlDialect>();

        var result = await new LogQueries().GetLogs(
            Operations,
            _factory,
            default,
            metadataId: metaId,
            sqlDialect: dialect
        );

        result.IsEstimatedCount.Should().BeFalse();
        result.TotalCount.Should().Be(LargeTableRows);
    }

    [Test]
    public async Task GetLogs_ProviderWithoutAnEstimate_CountsExactly()
    {
        var metaId = await SeedMetadata();
        await SeedLargeAnalyzedLogTable(metaId);
        // Sqlite's dialect returns no estimate SQL, and InMemory registers no dialect at all.
        var noEstimate = Substitute.For<ISqlDialect>();
        noEstimate.EstimateRowCount().Returns((string?)null);

        var withoutEstimate = await new LogQueries().GetLogs(
            Operations,
            _factory,
            default,
            sqlDialect: noEstimate
        );
        var withoutDialect = await new LogQueries().GetLogs(Operations, _factory, default);

        withoutEstimate.IsEstimatedCount.Should().BeFalse();
        withoutEstimate.TotalCount.Should().Be(LargeTableRows);
        withoutDialect.IsEstimatedCount.Should().BeFalse();
        withoutDialect.TotalCount.Should().Be(LargeTableRows);
    }

    [Test]
    public async Task Logs_ThroughGraphQL_UsesTheRegisteredDialectsEstimate()
    {
        var metaId = await SeedMetadata();
        await SeedLargeAnalyzedLogTable(metaId);

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);
        var services = new ServiceCollection().AddDevelopmentEnvironment();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(_factory);
        services.AddSingleton(_provider.GetRequiredService<ISqlDialect>());
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
        services.AddScoped(_ => Operations);
        await using var serviceProvider = services.BuildServiceProvider();
        var executor = await serviceProvider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var result = (HotChocolate.Execution.OperationResult)
            await executor.ExecuteAsync(
                "{ operations { logs { logs(take: 1) { totalCount isEstimatedCount } } } }"
            );

        result.Errors.Should().BeNullOrEmpty();
        using var document = JsonDocument.Parse(result.ToJson());
        var page = document
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("logs")
            .GetProperty("logs");
        page.GetProperty("isEstimatedCount")
            .GetBoolean()
            .Should()
            .BeTrue("HotChocolate injects the provider's dialect into the resolver");
    }

    [Test]
    public void OperationsQueries_LogsNamespace_ReturnsNewInstance()
    {
        new OperationsQueries().Logs().Should().NotBeNull();
    }
}
