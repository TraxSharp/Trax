using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Stress.Fixtures;

namespace Trax.Scheduler.Tests.Stress.IntegrationTests;

/// <summary>
/// The log read the dashboard's log grids and the GraphQL logs query share, timed over a log
/// table of millions of rows: the text filters, oldest-first reads, the cursor in both
/// directions, and the count over the same filters.
/// </summary>
/// <remarks>
/// The Postgres migrations index the lowered message and category with trigrams, so a term almost
/// no row carries is found from the index, and a term most rows carry is read in id order until
/// a page fills. An exact count with a text filter still reads every row the term matches, so a
/// common term's exact count is the slow read here; the capped count a pager uses stops at
/// <see cref="OperationsService.LogCountCap"/> matches and is held to <see cref="CountBudget"/>.
/// The timings are printed so a regression is visible; every other read is held to
/// <see cref="TestSetup.QueryTimeout"/>.
/// Run with: <c>dotnet test --filter "TestCategory=Stress"</c>; the row count is
/// <c>TRAX_STRESS_LOG_ROWS</c> (3,000,000 by default).
/// </remarks>
[Explicit(
    "Stress suite: seeds and loads heavily. Run with dotnet test --filter TestCategory=Stress"
)]
public class LogQueryStressTests : TestSetup
{
    private static readonly int Rows = int.TryParse(
        Environment.GetEnvironmentVariable("TRAX_STRESS_LOG_ROWS"),
        System.Globalization.CultureInfo.InvariantCulture,
        out var rows
    )
        ? rows
        : 3_000_000;

    /// <summary>What a pager's count may take, the budget of the reads a page makes.</summary>
    private static readonly TimeSpan CountBudget = TimeSpan.FromMilliseconds(300);

    [Test]
    public async Task Log_reads_stay_paged_over_millions_of_rows()
    {
        var db = (DbContext)DataContext;
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        var seed = Stopwatch.StartNew();
        // Every message names an order and a customer, about one in a hundred contains
        // "customer 42", and one in a million carries a rare marker. Categories cycle through 50
        // names.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO trax.log (metadata_id, event_id, level, message, category)
            SELECT 0, g % 7,
                   (ARRAY['debug','information','warning','error'])[1 + g % 4]::trax.log_level,
                   'Processed order ' || g || ' for customer ' || (g % 1000)
                       || CASE WHEN g % 1000000 = 777 THEN ' needle-marker' ELSE '' END,
                   'Trax.Stress.Category' || (g % 50)
            FROM generate_series(1, {Rows}) g
            """
        );
        await db.Database.ExecuteSqlRawAsync("ANALYZE trax.log");
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Seeded {Rows:N0} log rows in {seed.ElapsedMilliseconds} ms"
                )
            )
            .ConfigureAwait(false);

        var operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();

        try
        {
            await Measure("newest first, unfiltered", operations, new LogQuery())
                .ConfigureAwait(false);
            var newest = await operations
                .GetLogsAsync(new LogQuery(), CancellationToken.None)
                .ConfigureAwait(false);
            await Measure(
                    "newest first, cursor",
                    operations,
                    new LogQuery(AfterId: newest.NextCursor)
                )
                .ConfigureAwait(false);
            await Measure(
                    "oldest first, unfiltered",
                    operations,
                    new LogQuery { Order = LogOrder.OldestFirst }
                )
                .ConfigureAwait(false);
            await Measure(
                    "oldest first, cursor",
                    operations,
                    new LogQuery(AfterId: Rows / 2) { Order = LogOrder.OldestFirst }
                )
                .ConfigureAwait(false);
            await Measure(
                    "message contains a common term",
                    operations,
                    new LogQuery { MessageContains = "ORDER" }
                )
                .ConfigureAwait(false);
            await Measure(
                    "message contains a 1-in-100 term, oldest first",
                    operations,
                    new LogQuery { MessageContains = "customer 42", Order = LogOrder.OldestFirst }
                )
                .ConfigureAwait(false);
            await Measure(
                    "category contains, newest first",
                    operations,
                    new LogQuery { CategoryContains = "category49" }
                )
                .ConfigureAwait(false);
            await Measure(
                    "message contains a 1-in-a-million term",
                    operations,
                    new LogQuery { MessageContains = "needle-marker" }
                )
                .ConfigureAwait(false);
            await Measure(
                    "message contains a term no row has",
                    operations,
                    new LogQuery { MessageContains = "absent-term" }
                )
                .ConfigureAwait(false);
            // Order numbers starting 12 are about 111,000 rows, nearly all of them between
            // 1,200,000 and 1,299,999: common, but only far from the newest rows.
            await Measure(
                    "message contains a common term found only in old rows",
                    operations,
                    new LogQuery { MessageContains = "order 12" }
                )
                .ConfigureAwait(false);
            await Measure(
                    "category contains a common term, oldest first",
                    operations,
                    new LogQuery { CategoryContains = "category4", Order = LogOrder.OldestFirst }
                )
                .ConfigureAwait(false);
            await MeasureCappedCount(
                    "capped count, message contains a common term",
                    operations,
                    new LogQuery { MessageContains = "order" }
                )
                .ConfigureAwait(false);
            await MeasureCappedCount(
                    "capped count, message contains a 1-in-100 term",
                    operations,
                    new LogQuery { MessageContains = "customer 42" }
                )
                .ConfigureAwait(false);
            await MeasureCappedCount(
                    "capped count, category contains",
                    operations,
                    new LogQuery { CategoryContains = "category49" }
                )
                .ConfigureAwait(false);
            await MeasureCappedCount(
                    "capped count, message contains a 1-in-a-million term",
                    operations,
                    new LogQuery { MessageContains = "needle-marker" }
                )
                .ConfigureAwait(false);
            await MeasureCount(
                    "exact count, message contains a common term",
                    operations,
                    new LogQuery { MessageContains = "order" }
                )
                .ConfigureAwait(false);
            await MeasureCount(
                    "exact count, category contains",
                    operations,
                    new LogQuery { CategoryContains = "category49" }
                )
                .ConfigureAwait(false);
        }
        finally
        {
            // The other fixtures delete row by row before each test; leave them an empty table.
            await db.Database.ExecuteSqlRawAsync("TRUNCATE trax.log").ConfigureAwait(false);
        }
    }

    private static async Task Measure(string label, IOperationsService operations, LogQuery query)
    {
        LogPage page = null!;
        var elapsed = await AssertCompletesWithin(async () =>
                page = await operations
                    .GetLogsAsync(query, CancellationToken.None)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}: {elapsed.TotalMilliseconds:F0} ms, {page.Items.Count} row(s)"
                )
            )
            .ConfigureAwait(false);
    }

    private static async Task MeasureCappedCount(
        string label,
        IOperationsService operations,
        LogQuery query
    )
    {
        LogCount count = null!;
        var elapsed = await AssertCompletesWithin(
                async () =>
                    count = await operations
                        .CountLogsCappedAsync(query, CancellationToken.None)
                        .ConfigureAwait(false),
                CountBudget
            )
            .ConfigureAwait(false);
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}: {elapsed.TotalMilliseconds:F0} ms, {count.Count:N0}{(count.Capped ? "+" : "")}"
                )
            )
            .ConfigureAwait(false);
    }

    private static async Task MeasureCount(
        string label,
        IOperationsService operations,
        LogQuery query
    )
    {
        var count = 0;
        var elapsed = await AssertCompletesWithin(async () =>
                count = await operations
                    .CountLogsAsync(query, CancellationToken.None)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}: {elapsed.TotalMilliseconds:F0} ms, {count:N0}"
                )
            )
            .ConfigureAwait(false);
    }
}
