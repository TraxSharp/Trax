using System.Diagnostics;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radzen;
using Trax.Dashboard.Models;
using Trax.Dashboard.Tests.Stress.Fixtures;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Postgres.Services.PostgresContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Stress.IntegrationTests;

/// <summary>
/// The dashboard's busiest grids against Postgres at a million runs, the newest of them carrying
/// 1 MiB inputs and outputs, and a million log entries. Each grid reloads its page on every poll
/// tick, so a tick has to read only the columns the grid shows and must not count the whole table
/// again. The log grids read through the operations service, so their timings are the service's.
/// </summary>
/// <remarks>
/// Explicit: seeding takes minutes and needs Postgres. Run it with
/// <code>TRAX_TEST_PG_PORT=5433 dotnet test --filter TestCategory=Stress</code>
/// <c>TRAX_STRESS_CONNECTION</c> overrides the database, and the <c>TRAX_STRESS_*</c> variables in
/// <see cref="GridStressProfile"/> the row counts.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit("Stress suite: seeds a million rows. Run with dotnet test --filter TestCategory=Stress")]
public class GridQueryStressTests
{
    /// <summary>Budget for a poll tick: one page, its total remembered.</summary>
    private static readonly TimeSpan TickBudget = TimeSpan.FromMilliseconds(300);

    /// <summary>Budget for a first load or a new filter, which counts the matching rows.</summary>
    private static readonly TimeSpan CountedLoadBudget = TimeSpan.FromMilliseconds(1500);

    private static readonly GridStressProfile Profile = GridStressProfile.FromEnvironment();

    private static readonly Regex BlobColumn = new(
        @"\.(input|output|stack_trace|properties|exclusions)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("TRAX_STRESS_CONNECTION")
        ?? $"Host=localhost;Port={TestPostgres.Port};Database=trax_dashboard_stress;Username=trax;Password=trax123;"
            + "Maximum Pool Size=16;Timeout=30;Command Timeout=1200";

    private readonly SqlCapture _sql = new();
    private ServiceProvider _services = null!;
    private IDataContextProviderFactory _data = null!;
    private IOperationsService _operations = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        GridStressSeeder.EnsureDatabaseExists(ConnectionString);

        var services = new ServiceCollection().AddLogging(x => x.SetMinimumLevel(LogLevel.Warning));
        services.AddTrax(trax => trax.AddEffects(effects => effects.UsePostgres(ConnectionString)));
        services.ConfigureDbContext<PostgresContext>(options => options.AddInterceptors(_sql));
        _services = services.BuildServiceProvider();
        _data = _services.GetRequiredService<IDataContextProviderFactory>();
        _operations = new OperationsService(
            new TrainDiscoveryService(new ServiceCollection()),
            _data,
            new SchedulerConfiguration(),
            trainExecution: null!
        );

        await GridStressSeeder.SeedAsync(
            ConnectionString,
            Profile,
            message => TestContext.Progress.WriteLine($"[seed] {message}")
        );

        // Warm the connection pool and the query plans, so a budget times the query, not the
        // first connection.
        await RunsPageAsync(new GridCount());
        await LogsPageAsync(new GridCount(), new LogQuery(), LogOrder.NewestFirst);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _services.DisposeAsync();

    [Test]
    public async Task Runs_list_tick_selects_no_blob_column_and_counts_nothing()
    {
        var count = new GridCount();
        await RunsPageAsync(count);

        _sql.Clear();
        var tick = await RunsPageAsync(count);

        tick.Items.Should().HaveCount(20);
        tick.TotalCount.Should().Be((int)Profile.Metadata);
        _sql.Commands.Should().ContainSingle("a tick reads its page and nothing else");
        _sql.Commands.Single()
            .Should()
            .NotMatchRegex(BlobColumn.ToString(), "the grid does not show the input or output");
        _sql.Commands.Single().Should().NotContainEquivalentOf("count(");
    }

    [Test]
    public async Task Runs_list_first_load_and_tick_stay_within_budget()
    {
        var count = new GridCount();
        await MeasureAsync("runs list first load", CountedLoadBudget, () => RunsPageAsync(count));
        await MeasureAsync("runs list tick", TickBudget, () => RunsPageAsync(count));

        // For the record: the same page read as whole entities, as the grid read it before. Its
        // SQL selects the input and output, which is what the pattern the other tests check for
        // catches.
        _sql.Clear();
        var whole = await TimeAsync(async () =>
        {
            using var db = await _data.CreateDbContextAsync(default);
            await GridQueries.Runs(db, hideAdminTrains: true).Take(20).ToListAsync();
        });
        TestContext.Out.WriteLine(
            $"runs page read as whole entities: {whole.TotalMilliseconds:F0}ms"
        );
        _sql.Commands.Should().ContainSingle().Which.Should().MatchRegex(BlobColumn.ToString());
    }

    [Test]
    public async Task A_filter_on_a_projected_column_runs_in_the_database_within_budget()
    {
        var count = new GridCount();

        var page = await MeasureAsync(
            "runs list filtered by name",
            CountedLoadBudget,
            () =>
                RunsPageAsync(
                    count,
                    "x => (x.Name == null ? \"\" : x.Name).ToLower().Contains(\"istresstrain7\")"
                )
        );

        page.TotalCount.Should().BeGreaterThan(0);
        page.Items.Should().OnlyContain(r => r.Name.EndsWith('7'));
    }

    [Test]
    public async Task Group_runs_tick_selects_no_blob_column_within_budget()
    {
        var count = new GridCount();
        await LoadAsync(db => GridQueries.RunsOfGroup(db, 1), RunRow.Projection, count, 1L);

        _sql.Clear();
        await MeasureAsync(
            "group runs tick",
            TickBudget,
            () => LoadAsync(db => GridQueries.RunsOfGroup(db, 1), RunRow.Projection, count, 1L)
        );

        _sql.Commands.Should().OnlyContain(c => !BlobColumn.IsMatch(c) && !c.Contains("COUNT("));
    }

    [Test]
    public async Task Work_queue_tick_selects_no_input_within_budget()
    {
        var count = new GridCount();
        await LoadAsync(GridQueries.WorkQueue, WorkQueueRow.Projection, count, null);

        _sql.Clear();
        await MeasureAsync(
            "work queue tick",
            TickBudget,
            () => LoadAsync(GridQueries.WorkQueue, WorkQueueRow.Projection, count, null)
        );

        _sql.Commands.Should().OnlyContain(c => !BlobColumn.IsMatch(c) && !c.Contains("COUNT("));
    }

    [Test]
    public async Task Manifests_tick_selects_no_properties_within_budget()
    {
        var count = new GridCount();
        await LoadAsync(
            db => GridQueries.Manifests(db, hideAdminTrains: true),
            ManifestRow.Projection,
            count,
            true
        );

        _sql.Clear();
        await MeasureAsync(
            "manifests tick",
            TickBudget,
            () =>
                LoadAsync(
                    db => GridQueries.Manifests(db, hideAdminTrains: true),
                    ManifestRow.Projection,
                    count,
                    true
                )
        );

        _sql.Commands.Should().OnlyContain(c => !BlobColumn.IsMatch(c) && !c.Contains("COUNT("));
    }

    [Test]
    public async Task Logs_page_first_load_and_tick_stay_within_budget()
    {
        var count = new GridCount();
        var first = await MeasureAsync(
            "logs page first load",
            CountedLoadBudget,
            () => LogsPageAsync(count, new LogQuery(), LogOrder.NewestFirst)
        );
        first.TotalCount.Should().Be((int)Profile.Logs);
        first.Items.Should().BeInDescendingOrder(r => r.Id);

        // The page is the service's read, which returns each entry's stack trace for the API's
        // logs query though the grid does not show it. Log.Create caps a stack trace at 4,000
        // characters, so a page of 20 reads at most 80,000 more; the log table has no unbounded
        // column.
        _sql.Clear();
        await MeasureAsync(
            "logs page tick",
            TickBudget,
            () => LogsPageAsync(count, new LogQuery(), LogOrder.NewestFirst)
        );
        _sql.Commands.Should().ContainSingle("a tick reads its page and nothing else");
        _sql.Commands.Single().Should().NotContainEquivalentOf("count(");
    }

    [Test]
    public async Task A_runs_log_reads_oldest_first_within_budget()
    {
        var count = new GridCount();
        var filter = new LogQuery(MetadataId: Profile.BusyRun);

        var first = await MeasureAsync(
            "run log first load",
            CountedLoadBudget,
            () => LogsPageAsync(count, filter, LogOrder.OldestFirst)
        );
        first.TotalCount.Should().Be((int)(Profile.Logs / 50));
        first.Items.Should().BeInAscendingOrder(r => r.Id);
        first.Items.Should().OnlyContain(r => r.MetadataId == Profile.BusyRun);

        await MeasureAsync(
            "run log tick",
            TickBudget,
            () => LogsPageAsync(count, filter, LogOrder.OldestFirst)
        );
        await MeasureAsync(
            "run log, a page 10,000 entries in",
            TickBudget,
            () => LogsPageAsync(count, filter, LogOrder.OldestFirst, skip: 10_000)
        );
    }

    [TestCase("message 4242", false)]
    [TestCase("no such text anywhere", false)]
    [TestCase("category7", true)]
    public async Task A_text_filter_on_the_logs_page_stays_within_budget(
        string text,
        bool onCategory
    )
    {
        var filter = onCategory
            ? new LogQuery { CategoryContains = text }
            : new LogQuery { MessageContains = text };

        var page = await MeasureAsync(
            $"logs page filtered ({(onCategory ? "category" : "message")} contains '{text}')",
            CountedLoadBudget,
            () => LogsPageAsync(new GridCount(), filter, LogOrder.NewestFirst)
        );

        page.Items.Should()
            .OnlyContain(r =>
                (onCategory ? r.Category : r.Message).Contains(
                    text,
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    [Test]
    public async Task A_term_every_entry_carries_is_counted_up_to_the_cap_within_budget()
    {
        var capped = new LogCountCapped();
        var page = await MeasureAsync(
            "logs page filtered (message contains 'stress log', every entry)",
            CountedLoadBudget,
            () =>
                LogGridQuery.LoadPageAsync(
                    _operations,
                    new LogQuery { MessageContains = "stress log" },
                    new LoadDataArgs { Skip = 0, Top = 20 },
                    LogOrder.NewestFirst,
                    new GridCount(),
                    capped,
                    default
                )
        );

        page.TotalCount.Should().Be(OperationsService.LogCountCap);
        capped.Value.Should().BeTrue("a million entries match, more than the service counts");
    }

    [Test]
    public async Task A_text_filter_within_a_runs_log_stays_within_budget()
    {
        var page = await MeasureAsync(
            "run log filtered (message contains 'message 1')",
            CountedLoadBudget,
            () =>
                LogsPageAsync(
                    new GridCount(),
                    new LogQuery(MetadataId: Profile.BusyRun) { MessageContains = "message 1" },
                    LogOrder.OldestFirst
                )
        );

        page.TotalCount.Should().BeGreaterThan(0);
        page.Items.Should().OnlyContain(r => r.MetadataId == Profile.BusyRun);
    }

    private Task<ServerDataResult<LogRow>> LogsPageAsync(
        GridCount count,
        LogQuery filter,
        LogOrder order,
        int skip = 0
    ) =>
        LogGridQuery.LoadPageAsync(
            _operations,
            filter,
            new LoadDataArgs { Skip = skip, Top = 20 },
            order,
            count,
            new LogCountCapped(),
            default
        );

    private Task<ServerDataResult<RunRow>> RunsPageAsync(GridCount count, string? filter = null) =>
        LoadAsync(
            db => GridQueries.Runs(db, hideAdminTrains: true),
            RunRow.Projection,
            count,
            true,
            filter
        );

    private Task<ServerDataResult<TRow>> LoadAsync<TEntity, TRow>(
        Func<Trax.Effect.Data.Services.DataContext.IDataContext, IQueryable<TEntity>> query,
        System.Linq.Expressions.Expression<Func<TEntity, TRow>> projection,
        GridCount count,
        object? scope,
        string? filter = null
    )
        where TRow : class =>
        DataGridQueryHelper.LoadPageAsync(
            _data,
            query,
            projection,
            new LoadDataArgs
            {
                Skip = 0,
                Top = 20,
                Filter = filter,
            },
            count,
            scope,
            default
        );

    private static async Task<T> MeasureAsync<T>(
        string label,
        TimeSpan budget,
        Func<Task<T>> action
    )
    {
        var sw = Stopwatch.StartNew();
        var result = await action();
        sw.Stop();

        TestContext.Out.WriteLine(
            $"{label}: {sw.Elapsed.TotalMilliseconds:F0}ms (budget {budget.TotalMilliseconds:F0}ms, "
                + $"{Profile.Metadata:N0} runs)"
        );
        sw.Elapsed.Should()
            .BeLessThan(budget, $"{label} must stay within budget at {Profile.Metadata:N0} runs");
        return result;
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        return sw.Elapsed;
    }
}
