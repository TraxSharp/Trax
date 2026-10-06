using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The read models behind the dashboard's manifest detail cards, the manifest groups list's stat
/// columns and the logs page, as operations-service methods. The GraphQL API and the dashboard
/// each computed these with their own query; with one implementation here they cannot disagree.
/// The expected values are the ones Trax.Api's resolvers produce for the same rows.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class OperationsServiceReadModelTests : TestSetup
{
    private IOperationsService _operations = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
    }

    #region Manifest stats

    [Test]
    public async Task Manifest_stats_count_runs_by_state_and_find_the_last_runs()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group);
        var t0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        await SeedRun(manifest, TrainState.Completed, t0, t0.AddMinutes(1));
        await SeedRun(manifest, TrainState.Completed, t0.AddHours(1), t0.AddHours(1).AddMinutes(3));
        await SeedRun(manifest, TrainState.Failed, t0.AddHours(2), t0.AddHours(2).AddMinutes(1));
        await SeedRun(manifest, TrainState.InProgress, t0.AddHours(3));
        await SeedRun(manifest, TrainState.Pending, t0.AddHours(4));
        await SeedRun(manifest, TrainState.Cancelled, t0.AddHours(5), t0.AddHours(5));
        var other = await SeedManifest(group);
        await SeedRun(other, TrainState.Completed, t0.AddDays(1), t0.AddDays(1).AddMinutes(1));

        var stats = await _operations.GetManifestExecutionStatsAsync(
            manifest.Id,
            CancellationToken.None
        );

        stats
            .Should()
            .Be(
                new ManifestExecutionStats(
                    manifest.Id,
                    Total: 6,
                    Completed: 2,
                    Failed: 1,
                    InProgress: 1,
                    Pending: 1,
                    Cancelled: 1,
                    LastRun: t0.AddHours(5),
                    LastSuccessfulRun: t0.AddHours(1).AddMinutes(3)
                )
            );
    }

    [Test]
    public async Task Manifest_stats_for_a_manifest_with_no_runs_are_zero()
    {
        var stats = await _operations.GetManifestExecutionStatsAsync(
            424242,
            CancellationToken.None
        );

        stats.Should().Be(new ManifestExecutionStats(424242, 0, 0, 0, 0, 0, 0, null, null));
    }

    #endregion

    #region Group stats

    [Test]
    public async Task Group_stats_are_one_row_per_distinct_id_in_the_order_given()
    {
        var busy = await CreateAndSaveManifestGroup(DataContext, $"busy-{Guid.NewGuid():N}");
        var idle = await CreateAndSaveManifestGroup(DataContext, $"idle-{Guid.NewGuid():N}");
        var empty = await CreateAndSaveManifestGroup(DataContext, $"empty-{Guid.NewGuid():N}");
        var t0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var a = await SeedManifest(busy);
        var b = await SeedManifest(busy);
        await SeedManifest(idle);
        await SeedRun(a, TrainState.Completed, t0, t0.AddMinutes(1));
        await SeedRun(a, TrainState.Failed, t0.AddHours(1), t0.AddHours(1));
        await SeedRun(b, TrainState.InProgress, t0.AddHours(2));
        await SeedRun(b, TrainState.Pending, t0.AddHours(3));

        var stats = await _operations.GetManifestGroupExecutionStatsAsync(
            [empty.Id, busy.Id, idle.Id, busy.Id],
            CancellationToken.None
        );

        stats
            .Should()
            .Equal(
                new ManifestGroupExecutionStats(empty.Id, 0, 0, 0, 0, 0, null),
                new ManifestGroupExecutionStats(
                    busy.Id,
                    ManifestCount: 2,
                    TotalExecutions: 4,
                    Completed: 1,
                    Failed: 1,
                    InProgress: 1,
                    LastRun: t0.AddHours(3)
                ),
                new ManifestGroupExecutionStats(idle.Id, 1, 0, 0, 0, 0, null)
            );
    }

    [Test]
    public async Task Group_stats_for_no_ids_are_empty()
    {
        var stats = await _operations.GetManifestGroupExecutionStatsAsync(
            [],
            CancellationToken.None
        );

        stats.Should().BeEmpty();
    }

    [Test]
    public async Task Group_stats_refuse_more_ids_than_a_batch()
    {
        var ids = Enumerable.Range(1, OperationsService.MaxBatchSize + 1).Select(i => (long)i);

        var act = () =>
            _operations.GetManifestGroupExecutionStatsAsync(ids.ToList(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Logs

    [Test]
    public async Task Logs_are_newest_first_and_filter_by_run_level_and_category()
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        var otherRun = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, 3, LogLevel.Information, "Alpha");
        await SeedLogs(run.Id, 2, LogLevel.Error, "Alpha");
        await SeedLogs(run.Id, 1, LogLevel.Error, "Beta");
        await SeedLogs(otherRun.Id, 4, LogLevel.Error, "Alpha");

        var page = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, MinimumLevel: LogLevel.Warning, Category: "Alpha"),
            CancellationToken.None
        );

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(l => l.MetadataId == run.Id);
        page.Items.Should().OnlyContain(l => l.Level == LogLevel.Error && l.Category == "Alpha");
        page.Items.Select(l => l.Id).Should().BeInDescendingOrder();
        page.NextCursor.Should().Be(page.Items[^1].Id);

        (
            await _operations.CountLogsAsync(
                new LogQuery(MetadataId: run.Id, MinimumLevel: LogLevel.Warning, Category: "Alpha"),
                CancellationToken.None
            )
        )
            .Should()
            .Be(2);
    }

    [Test]
    public async Task Logs_page_by_cursor_without_overlap()
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, 7, LogLevel.Information, "Paged");

        var first = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Take: 4),
            CancellationToken.None
        );
        var second = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, AfterId: first.NextCursor, Skip: 99, Take: 4),
            CancellationToken.None
        );

        first.Items.Should().HaveCount(4);
        second.Items.Should().HaveCount(3, "a cursor ignores the offset");
        second.Skip.Should().Be(0);
        first.Items.Select(l => l.Id).Should().NotIntersectWith(second.Items.Select(l => l.Id));
        (
            await _operations.CountLogsAsync(
                new LogQuery(MetadataId: run.Id, AfterId: first.NextCursor, Take: 1),
                CancellationToken.None
            )
        )
            .Should()
            .Be(7, "the count covers the filter, not the page or the cursor");
    }

    [Test]
    public async Task Logs_page_by_offset()
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, 5, LogLevel.Information, "Offset");

        var all = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Take: 5),
            CancellationToken.None
        );
        var skipped = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Skip: 2, Take: 5),
            CancellationToken.None
        );

        skipped.Skip.Should().Be(2);
        skipped.Items.Select(l => l.Id).Should().Equal(all.Items.Skip(2).Select(l => l.Id));
    }

    [TestCase(int.MaxValue, OperationsService.MaxPageSize, TestName = "A_huge_take_is_clamped")]
    [TestCase(0, 1, TestName = "A_zero_take_reads_one")]
    [TestCase(-5, 1, TestName = "A_negative_take_reads_one")]
    public async Task Log_page_size_is_clamped(int take, int expected)
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, OperationsService.MaxPageSize + 1, LogLevel.Information, "Clamp");

        var page = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Take: take),
            CancellationToken.None
        );

        page.Take.Should().Be(expected);
        page.Items.Should().HaveCount(expected, "one call never materialises the whole table");
    }

    [Test]
    public async Task Logs_filter_by_text_the_message_contains_ignoring_case()
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLog(run.Id, "Payment REFUSED for order 7", "Shop.Payments");
        await SeedLog(run.Id, "payment accepted", "Shop.Payments");
        await SeedLog(run.Id, "refused: no stock", "Shop.Stock");

        var page = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id) { MessageContains = "refused" },
            CancellationToken.None
        );
        var count = await _operations.CountLogsAsync(
            new LogQuery(MetadataId: run.Id) { MessageContains = "REFUSED" },
            CancellationToken.None
        );

        page.Items.Select(l => l.Message)
            .Should()
            .Equal("refused: no stock", "Payment REFUSED for order 7");
        count.Should().Be(2, "the count takes the same text filter");
    }

    [Test]
    public async Task Logs_filter_by_text_the_category_contains_ignoring_case()
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLog(run.Id, "one", "Shop.Payments.Gateway");
        await SeedLog(run.Id, "two", "Shop.Stock");
        await SeedLog(run.Id, "three", "shop.payments");

        var page = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id) { CategoryContains = "PAYMENTS" },
            CancellationToken.None
        );

        page.Items.Select(l => l.Message).Should().BeEquivalentTo(["one", "three"]);
        (
            await _operations.CountLogsAsync(
                new LogQuery(MetadataId: run.Id)
                {
                    CategoryContains = "payments",
                    MessageContains = "three",
                },
                CancellationToken.None
            )
        )
            .Should()
            .Be(1, "both text filters apply together");
    }

    [TestCase("50%", "Disk at 50% capacity", TestName = "A_percent_sign_matches_itself")]
    [TestCase("a_b", "key a_b missing", TestName = "An_underscore_matches_itself")]
    [TestCase(@"C:\temp", @"wrote C:\temp\out.txt", TestName = "A_backslash_matches_itself")]
    public async Task Log_text_filters_match_wildcards_literally(string term, string expected)
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLog(run.Id, "Disk at 50% capacity", "Wildcards");
        await SeedLog(run.Id, "Disk at 500 MB", "Wildcards");
        await SeedLog(run.Id, "key a_b missing", "Wildcards");
        await SeedLog(run.Id, "key axb missing", "Wildcards");
        await SeedLog(run.Id, @"wrote C:\temp\out.txt", "Wildcards");
        await SeedLog(run.Id, @"wrote C:temp", "Wildcards");

        var page = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id) { MessageContains = term },
            CancellationToken.None
        );

        page.Items.Select(l => l.Message).Should().Equal(expected);
    }

    [Test]
    public async Task Logs_read_oldest_first_and_page_forward_by_cursor()
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, 7, LogLevel.Information, "Ascending");

        var first = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Take: 4) { Order = LogOrder.OldestFirst },
            CancellationToken.None
        );
        var second = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, AfterId: first.NextCursor, Take: 4)
            {
                Order = LogOrder.OldestFirst,
            },
            CancellationToken.None
        );
        var newestFirst = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Take: 10),
            CancellationToken.None
        );

        first.Items.Select(l => l.Id).Should().BeInAscendingOrder();
        second.Items.Should().HaveCount(3);
        first
            .Items.Concat(second.Items)
            .Select(l => l.Id)
            .Should()
            .Equal(newestFirst.Items.Select(l => l.Id).Reverse(), "the same rows, oldest first");
    }

    [Test]
    public async Task Logs_read_oldest_first_page_by_offset()
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, 5, LogLevel.Information, "AscendingOffset");

        var all = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Take: 5) { Order = LogOrder.OldestFirst },
            CancellationToken.None
        );
        var skipped = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Skip: 3, Take: 5) { Order = LogOrder.OldestFirst },
            CancellationToken.None
        );

        skipped.Items.Select(l => l.Id).Should().Equal(all.Items.Skip(3).Select(l => l.Id));
    }

    [TestCase("", TestName = "An_empty_text_filter_matches_everything")]
    [TestCase(null, TestName = "A_null_text_filter_matches_everything")]
    public async Task An_empty_text_filter_is_no_filter(string? term)
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, 3, LogLevel.Information, "Empty");

        (
            await _operations.CountLogsAsync(
                new LogQuery(MetadataId: run.Id)
                {
                    MessageContains = term,
                    CategoryContains = term,
                },
                CancellationToken.None
            )
        )
            .Should()
            .Be(3);
    }

    [TestCase(LogOrder.NewestFirst)]
    [TestCase(LogOrder.OldestFirst)]
    public async Task A_text_filtered_page_reads_matches_on_both_sides_of_the_window_in_order(
        LogOrder order
    )
    {
        // More rows than the window, with matches near each end and in the middle, so a page
        // starts in the window and finishes past it, whichever end it reads from.
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        var rows = OperationsService.LogTextWindow + 20_000;
        var ctx = (DbContext)DataContext;
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO trax.log (metadata_id, event_id, level, message, category)
            SELECT {run.Id}, 0, 'information'::trax.log_level,
                   'line ' || g || CASE WHEN g % 1000 = 7 OR g IN (3, {rows
                - 2}) THEN ' Needle' ELSE '' END,
                   'Window'
            FROM generate_series(1, {rows}) g
            """
        );
        var matching = await DataContext
            .Logs.AsNoTracking()
            .Where(l => l.MetadataId == run.Id && l.Message.EndsWith(" Needle"))
            .Select(l => l.Id)
            .ToListAsync();
        var expected =
            order == LogOrder.OldestFirst
                ? matching.Order().ToList()
                : matching.OrderDescending().ToList();

        var whole = await _operations.GetLogsAsync(
            new LogQuery(MetadataId: run.Id, Take: OperationsService.MaxPageSize)
            {
                MessageContains = "needle",
                Order = order,
            },
            CancellationToken.None
        );
        whole.Items.Select(l => l.Id).Should().Equal(expected);

        var paged = new List<long>();
        long? cursor = null;
        do
        {
            var page = await _operations.GetLogsAsync(
                new LogQuery(MetadataId: run.Id, Take: 7, AfterId: cursor)
                {
                    MessageContains = "needle",
                    Order = order,
                },
                CancellationToken.None
            );
            paged.AddRange(page.Items.Select(l => l.Id));
            cursor = page.Items.Count == 7 ? page.NextCursor : null;
        } while (cursor is not null);

        paged.Should().Equal(expected, "paging by cursor reads the same rows in the same order");
    }

    #endregion

    #region Helpers

    private async Task<Manifest> SeedManifest(ManifestGroup group)
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "stats" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task<Metadata> SeedRun(
        Manifest? manifest,
        TrainState state,
        DateTime start,
        DateTime? end = null
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifest?.Id,
            }
        );
        metadata.TrainState = state;
        metadata.StartTime = start;
        metadata.EndTime = end;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return metadata;
    }

    [TestCase(OperationsService.LogCountCap, OperationsService.LogCountCap, false)]
    [TestCase(OperationsService.LogCountCap + 1, OperationsService.LogCountCap, true)]
    public async Task A_text_filtered_count_stops_at_the_cap(
        int matching,
        int expectedCount,
        bool expectedCapped
    )
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, matching, LogLevel.Information, "Shop.Capped");
        await SeedLogs(run.Id, 5, LogLevel.Information, "Shop.Other");
        var query = new LogQuery(MetadataId: run.Id) { CategoryContains = "capped" };

        var count = await _operations.CountLogsCappedAsync(query, CancellationToken.None);

        count.Should().Be(new LogCount(expectedCount, expectedCapped));
        (await _operations.CountLogsAsync(query, CancellationToken.None))
            .Should()
            .Be(matching, "the exact count is not capped");
    }

    [Test]
    public async Task A_count_without_a_text_filter_is_exact_past_the_cap()
    {
        var run = await SeedRun(null, TrainState.Completed, DateTime.UtcNow, DateTime.UtcNow);
        await SeedLogs(run.Id, OperationsService.LogCountCap + 1, LogLevel.Information, "Wide");

        var count = await _operations.CountLogsCappedAsync(
            new LogQuery(MetadataId: run.Id, Category: "Wide"),
            CancellationToken.None
        );

        count.Should().Be(new LogCount(OperationsService.LogCountCap + 1, Capped: false));
    }

    private async Task SeedLog(long metadataId, string message, string category)
    {
        var ctx = (DbContext)DataContext;
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO trax.log (metadata_id, event_id, level, message, category) VALUES ({metadataId}, 0, 'information'::trax.log_level, {message}, {category})"
        );
    }

    private async Task SeedLogs(long metadataId, int count, LogLevel level, string category)
    {
        // Log.MetadataId has a private setter; the framework's logger provider writes it
        // directly, so seed the same way.
        var ctx = (DbContext)DataContext;
        var levelName = level.ToString().ToLowerInvariant();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO trax.log (metadata_id, event_id, level, message, category) SELECT {metadataId}, g, {levelName}::trax.log_level, 'msg-' || g, {category} FROM generate_series(1, {count}) g"
        );
    }

    #endregion
}
