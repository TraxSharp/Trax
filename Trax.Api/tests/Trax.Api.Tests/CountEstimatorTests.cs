using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;

namespace Trax.Api.Tests;

/// <summary>
/// Every operations list whose total can be estimated (executions, manifests, work queues and
/// groups, alongside logs in <see cref="LogQueriesTests"/>) reports the database's row estimate for
/// an unfiltered list of a large table, the same estimate on every page, and an exact count as
/// soon as a filter narrows it. Runs on Postgres, whose dialect reads <c>pg_class.reltuples</c>.
/// </summary>
[TestFixture]
public class CountEstimatorTests
{
    private static readonly string ConnectionString =
        $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_operations;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=30;"
        + "Timeout=30;Tcp Keepalive=true";

    // Above CountEstimator's threshold, so the database's estimate is used.
    private const int LargeTableRows = CountEstimator.EstimateThreshold + 2_000;

    // reltuples is a sampled estimate; ANALYZE of a table this size reads all of it, so the
    // estimate is close, but the tolerance keeps the test from pinning the planner's arithmetic.
    private const int Tolerance = 500;

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;
    private ISqlDialect _dialect = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t => t.AddEffects(e => e.UsePostgres(ConnectionString)));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        _dialect = _provider.GetRequiredService<ISqlDialect>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [SetUp]
    public Task SetUp() =>
        ExecAsync(
            "TRUNCATE TABLE trax.dead_letter, trax.work_queue, trax.metadata, trax.manifest, "
                + "trax.manifest_group RESTART IDENTITY CASCADE"
        );

    private async Task ExecAsync(string sql)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        await ((DbContext)db).Database.ExecuteSqlRawAsync(sql);
    }

    private Task SeedGroupsAsync(int count) =>
        ExecAsync(
            "INSERT INTO trax.manifest_group (name) "
                + $"SELECT 'estimate-group-' || g FROM generate_series(1, {count}) g"
        );

    private async Task SeedManifestsAsync()
    {
        await SeedGroupsAsync(1);
        await ExecAsync(
            "INSERT INTO trax.manifest (external_id, name, manifest_group_id, schedule_type, is_enabled) "
                + "SELECT 'estimate-manifest-' || g, 'Trax.Estimate.ITrain', 1, 'none'::trax.schedule_type, "
                + $"       g % 2 = 0 FROM generate_series(1, {LargeTableRows}) g"
        );
    }

    private Task SeedExecutionsAsync() =>
        ExecAsync(
            "INSERT INTO trax.metadata (external_id, name, train_state, start_time) "
                + "SELECT lpad(g::text, 32, '0'), 'Trax.Estimate.ITrain', "
                + "       (CASE WHEN g % 4 = 0 THEN 'failed' ELSE 'completed' END)::trax.train_state, now() "
                + $"FROM generate_series(1, {LargeTableRows}) g"
        );

    private Task SeedWorkQueuesAsync() =>
        ExecAsync(
            "INSERT INTO trax.work_queue (external_id, train_name, status, created_at, priority) "
                + "SELECT 'estimate-wq-' || g, 'Trax.Estimate.ITrain', "
                + "       (CASE WHEN g % 4 = 0 THEN 'cancelled' ELSE 'dispatched' END)::trax.work_queue_status, now(), 0 "
                + $"FROM generate_series(1, {LargeTableRows}) g"
        );

    private Task AnalyzeAsync(string table) => ExecAsync($"ANALYZE trax.{table}");

    private static void ShouldBeTheEstimateOnEveryPage<T>(
        PagedResult<T> first,
        PagedResult<T> second
    )
    {
        first.IsEstimatedCount.Should().BeTrue("an unfiltered total of a large table is estimated");
        first.TotalCount.Should().BeCloseTo(LargeTableRows, Tolerance);
        second
            .IsEstimatedCount.Should()
            .BeTrue("the cursor does not change how the whole list is counted");
        second.TotalCount.Should().Be(first.TotalCount, "totalCount does not depend on the page");
    }

    [Test]
    public async Task Executions_UnfilteredTotalOfALargeTable_IsTheDatabasesEstimateOnEveryPage()
    {
        await SeedExecutionsAsync();
        await AnalyzeAsync("metadata");
        var queries = new OperationsQueries();

        var first = await queries.GetExecutions(_factory, default, take: 10, sqlDialect: _dialect);
        var second = await queries.GetExecutions(
            _factory,
            default,
            take: 10,
            afterId: first.NextCursor,
            sqlDialect: _dialect
        );

        ShouldBeTheEstimateOnEveryPage(first, second);
    }

    [Test]
    public async Task Executions_FilteredTotalOfALargeTable_IsExact()
    {
        await SeedExecutionsAsync();
        await AnalyzeAsync("metadata");

        var result = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            trainState: TrainState.Failed,
            sqlDialect: _dialect
        );

        result.IsEstimatedCount.Should().BeFalse();
        result.TotalCount.Should().Be(LargeTableRows / 4);
    }

    [Test]
    public async Task Manifests_UnfilteredTotalOfALargeTable_IsTheDatabasesEstimateOnEveryPage()
    {
        await SeedManifestsAsync();
        await AnalyzeAsync("manifest");
        var queries = new OperationsQueries();

        var first = await queries.GetManifests(_factory, default, take: 10, sqlDialect: _dialect);
        var second = await queries.GetManifests(
            _factory,
            default,
            take: 10,
            afterId: first.NextCursor,
            sqlDialect: _dialect
        );

        ShouldBeTheEstimateOnEveryPage(first, second);
    }

    [Test]
    public async Task Manifests_FilteredTotalOfALargeTable_IsExact()
    {
        await SeedManifestsAsync();
        await AnalyzeAsync("manifest");

        var result = await new OperationsQueries().GetManifests(
            _factory,
            default,
            isEnabled: true,
            sqlDialect: _dialect
        );

        result.IsEstimatedCount.Should().BeFalse();
        result.TotalCount.Should().Be(LargeTableRows / 2);
    }

    [Test]
    public async Task WorkQueues_UnfilteredTotalOfALargeTable_IsTheDatabasesEstimateOnEveryPage()
    {
        await SeedWorkQueuesAsync();
        await AnalyzeAsync("work_queue");
        var queries = new WorkQueueQueries();

        var first = await queries.GetWorkQueues(_factory, default, take: 10, sqlDialect: _dialect);
        var second = await queries.GetWorkQueues(
            _factory,
            default,
            take: 10,
            afterId: first.NextCursor,
            sqlDialect: _dialect
        );

        ShouldBeTheEstimateOnEveryPage(first, second);
    }

    [Test]
    public async Task WorkQueues_FilteredTotalOfALargeTable_IsExact()
    {
        await SeedWorkQueuesAsync();
        await AnalyzeAsync("work_queue");

        var result = await new WorkQueueQueries().GetWorkQueues(
            _factory,
            default,
            status: WorkQueueStatus.Cancelled,
            sqlDialect: _dialect
        );

        result.IsEstimatedCount.Should().BeFalse();
        result.TotalCount.Should().Be(LargeTableRows / 4);
    }

    [Test]
    public async Task Groups_UnfilteredTotalOfALargeTable_IsTheDatabasesEstimateOnEveryPage()
    {
        await SeedGroupsAsync(LargeTableRows);
        await AnalyzeAsync("manifest_group");
        var queries = new ManifestGroupQueries();

        var first = await queries.GetGroups(_factory, default, take: 10, sqlDialect: _dialect);
        var second = await queries.GetGroups(
            _factory,
            default,
            take: 10,
            afterId: first.NextCursor,
            sqlDialect: _dialect
        );

        ShouldBeTheEstimateOnEveryPage(first, second);
    }

    [Test]
    public async Task Groups_FilteredTotalOfALargeTable_IsExact()
    {
        await SeedGroupsAsync(LargeTableRows);
        await AnalyzeAsync("manifest_group");

        var result = await new ManifestGroupQueries().GetGroups(
            _factory,
            default,
            nameContains: "group-1",
            sqlDialect: _dialect
        );

        result.IsEstimatedCount.Should().BeFalse();
        result
            .TotalCount.Should()
            .Be(Enumerable.Range(1, LargeTableRows).Count(g => g.ToString().StartsWith('1')));
    }

    [Test]
    public async Task ASmallTable_IsCountedExactlyEvenWithAnEstimate()
    {
        await SeedGroupsAsync(25);
        await AnalyzeAsync("manifest_group");

        var result = await new ManifestGroupQueries().GetGroups(
            _factory,
            default,
            sqlDialect: _dialect
        );

        result
            .IsEstimatedCount.Should()
            .BeFalse("below the threshold an estimate would visibly disagree with the rows shown");
        result.TotalCount.Should().Be(25);
    }

    [Test]
    public async Task ATableTheDatabaseHasNotMeasured_IsCountedExactly()
    {
        // Freshly truncated and never analyzed: Postgres reports reltuples as -1 (unknown), which
        // is no usable estimate. Autovacuum is held off so it cannot measure the table mid-test.
        await ExecAsync("ALTER TABLE trax.manifest_group SET (autovacuum_enabled = false)");
        try
        {
            await ExecAsync("TRUNCATE TABLE trax.manifest_group RESTART IDENTITY CASCADE");
            await SeedGroupsAsync(LargeTableRows);

            var result = await new ManifestGroupQueries().GetGroups(
                _factory,
                default,
                sqlDialect: _dialect
            );

            result.IsEstimatedCount.Should().BeFalse();
            result.TotalCount.Should().Be(LargeTableRows);
        }
        finally
        {
            await ExecAsync("ALTER TABLE trax.manifest_group RESET (autovacuum_enabled)");
        }
    }
}
