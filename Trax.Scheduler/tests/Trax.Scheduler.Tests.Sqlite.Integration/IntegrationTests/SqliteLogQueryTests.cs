using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The log read the dashboard's log grids and the GraphQL logs query share, on Sqlite: the text
/// filters match ignoring case and treat LIKE wildcards literally, and the log reads oldest first
/// with a cursor that pages forward, as it does on Postgres.
/// </summary>
[TestFixture]
public class SqliteLogQueryTests : TestSetup
{
    private IOperationsService? _operations;
    private string? _tag;

    public override async Task TestSetUp()
    {
        await base.TestSetUp().ConfigureAwait(false);
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
        // The data-context logger writes this host's own logs to the same table, so every term
        // carries a tag no other row has.
        _tag = $"t{Guid.NewGuid():N}";
    }

    [Test]
    public async Task Text_filters_match_ignoring_case_and_the_count_agrees()
    {
        await Seed($"{_tag.NotNull()} Payment REFUSED", "Shop.Payments");
        await Seed($"{_tag.NotNull()} payment accepted", "Shop.Payments");
        await Seed($"{_tag.NotNull()} refused: no stock", "Shop.Stock");

        var query = new LogQuery
        {
            MessageContains = $"{_tag.NotNull().ToUpperInvariant()} PAYMENT",
            CategoryContains = "payments",
        };
        var page = await _operations.NotNull().GetLogsAsync(query, CancellationToken.None);
        var count = await _operations.NotNull().CountLogsAsync(query, CancellationToken.None);

        page.Items.Select(l => l.Message)
            .Should()
            .Equal($"{_tag.NotNull()} payment accepted", $"{_tag.NotNull()} Payment REFUSED");
        count.Should().Be(2);
    }

    [Test]
    public async Task Wildcards_in_a_term_match_themselves()
    {
        await Seed($"{_tag.NotNull()} at 50% capacity", "Wildcards");
        await Seed($"{_tag.NotNull()} at 500 MB", "Wildcards");
        await Seed($"{_tag.NotNull()} key a_b", "Wildcards");
        await Seed($"{_tag.NotNull()} key axb", "Wildcards");

        (
            await _operations
                .NotNull()
                .CountLogsAsync(
                    new LogQuery { MessageContains = $"{_tag.NotNull()} at 50%" },
                    CancellationToken.None
                )
        )
            .Should()
            .Be(1);
        (
            await _operations
                .NotNull()
                .CountLogsAsync(
                    new LogQuery { MessageContains = $"{_tag.NotNull()} key a_b" },
                    CancellationToken.None
                )
        )
            .Should()
            .Be(1);
    }

    [Test]
    public async Task Oldest_first_pages_forward_by_cursor()
    {
        for (var i = 0; i < 5; i++)
            await Seed(
                string.Create(CultureInfo.InvariantCulture, $"{_tag.NotNull()} line {i}"),
                "Ascending"
            );

        var first = await _operations
            .NotNull()
            .GetLogsAsync(
                new LogQuery(Take: 3)
                {
                    MessageContains = _tag.NotNull(),
                    Order = LogOrder.OldestFirst,
                },
                CancellationToken.None
            );
        var second = await _operations
            .NotNull()
            .GetLogsAsync(
                new LogQuery(AfterId: first.NextCursor, Take: 3)
                {
                    MessageContains = _tag.NotNull(),
                    Order = LogOrder.OldestFirst,
                },
                CancellationToken.None
            );

        first
            .Items.Concat(second.Items)
            .Select(l => l.Message)
            .Should()
            .Equal(
                Enumerable
                    .Range(0, 5)
                    .Select(i =>
                        string.Create(CultureInfo.InvariantCulture, $"{_tag.NotNull()} line {i}")
                    )
            );
    }

    [TestCase(OperationsService.LogCountCap, OperationsService.LogCountCap, false)]
    [TestCase(OperationsService.LogCountCap + 1, OperationsService.LogCountCap, true)]
    public async Task A_text_filtered_count_stops_at_the_cap(
        int matching,
        int expectedCount,
        bool expectedCapped
    )
    {
        await SeedMany(matching, $"{_tag.NotNull()} capped");
        var query = new LogQuery { MessageContains = _tag.NotNull() };

        (await _operations.NotNull().CountLogsCappedAsync(query, CancellationToken.None))
            .Should()
            .Be(new LogCount(expectedCount, expectedCapped));
        (await _operations.NotNull().CountLogsAsync(query, CancellationToken.None))
            .Should()
            .Be(matching);
    }

    private async Task SeedMany(int count, string message)
    {
        for (var i = 0; i < count; i++)
            await DataContext
                .Track(
                    Log.Create(
                        new CreateLog
                        {
                            Level = LogLevel.Information,
                            Message = message,
                            CategoryName = "Capped",
                            EventId = 0,
                        }
                    )
                )
                .ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
    }

    private async Task Seed(string message, string category)
    {
        await DataContext
            .Track(
                Log.Create(
                    new CreateLog
                    {
                        Level = LogLevel.Information,
                        Message = message,
                        CategoryName = category,
                        EventId = 0,
                    }
                )
            )
            .ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
    }
}
