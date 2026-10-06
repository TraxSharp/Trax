using AwesomeAssertions;
using Radzen;
using Trax.Dashboard.Models;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Time;
using Trax.Dashboard.Utilities;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

/// <summary>
/// A server-paged grid reloads its page on every poll tick. The rows are read fresh each time,
/// but the total under the pager is counted again only when the count could have changed in a
/// way the page cannot tell: a different filter or scope, or after <see cref="GridCount.MaxAge"/>.
/// A short page is the last one, so it gives the exact total without counting.
/// </summary>
[TestFixture]
public class DataGridQueryHelperTests
{
    private InMemoryDataContextFactory _data = null!;
    private ManualClock _clock = null!;
    private GridCount _count = null!;

    [SetUp]
    public void SetUp()
    {
        _data = new InMemoryDataContextFactory();
        _clock = new ManualClock();
        _count = new GridCount(_clock);
    }

    [Test]
    public async Task A_poll_tick_with_the_same_filter_reuses_the_total()
    {
        await SeedAsync("a", 25);
        (await LoadAsync()).TotalCount.Should().Be(25);

        await SeedAsync("a", 5);
        var tick = await LoadAsync();

        tick.Items.Should().HaveCount(20, "the page itself is read fresh");
        tick.TotalCount.Should()
            .Be(25, "the total is not counted again on a tick with the same filter");
    }

    [Test]
    public async Task The_total_is_counted_again_once_it_is_older_than_the_max_age()
    {
        await SeedAsync("a", 25);
        await LoadAsync();
        await SeedAsync("a", 5);

        _clock.Advance(GridCount.MaxAge);

        (await LoadAsync()).TotalCount.Should().Be(30);
    }

    [Test]
    public async Task A_new_filter_is_counted_at_once()
    {
        await SeedAsync("a", 25);
        await SeedAsync("b", 22);
        await LoadAsync();

        (await LoadAsync(filter: "x => x.SubjectKey == \"b\"")).TotalCount.Should().Be(22);
    }

    [Test]
    public async Task A_new_scope_is_counted_at_once()
    {
        await SeedAsync("a", 25);
        await LoadAsync(scope: 1L);
        await SeedAsync("a", 5);

        (await LoadAsync(scope: 2L)).TotalCount.Should().Be(30);
    }

    [Test]
    public async Task A_short_last_page_gives_the_exact_total()
    {
        await SeedAsync("a", 25);
        await LoadAsync();
        await SeedAsync("a", 5);

        (await LoadAsync(skip: 20))
            .TotalCount.Should()
            .Be(30, "page 2 holds 10 rows, so there are 30 without counting");
    }

    [Test]
    public async Task A_page_past_the_remembered_total_counts_again()
    {
        await SeedAsync("a", 5);
        await LoadAsync(top: 10);
        await SeedAsync("a", 20);

        (await LoadAsync(top: 10))
            .TotalCount.Should()
            .Be(25, "a full page of 10 cannot sit under a total of 5");
    }

    private Task<ServerDataResult<WorkQueueRow>> LoadAsync(
        string? filter = null,
        object? scope = null,
        int skip = 0,
        int top = 20
    ) =>
        DataGridQueryHelper.LoadPageAsync(
            _data,
            db => db.WorkQueues.OrderByDescending(q => q.Id),
            WorkQueueRow.Projection,
            new LoadDataArgs
            {
                Filter = filter,
                Skip = skip,
                Top = top,
            },
            _count,
            scope,
            default
        );

    private async Task SeedAsync(string subject, int count)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
            db.WorkQueues.Add(
                WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = subject })
            );
        await db.SaveChanges(default);
    }
}
