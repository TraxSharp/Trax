using AwesomeAssertions;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The logs page and a run's logs grid read through <c>IOperationsService.GetLogsAsync</c> and
/// <c>CountLogsAsync</c>, the calls the API's logs query makes, newest first on the logs page and
/// oldest first on a run's page. Their filters are the ones <see cref="LogQuery"/> serves, set
/// above the grid; no column header offers a filter the read would ignore.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class LogGridTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private OperationsCallLog _operations = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        _operations = RecordingOperationsService.Register(_ctx.Services, _data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task The_logs_page_reads_newest_first_through_the_service()
    {
        var run = await SeedRunAsync();
        await SeedLogAsync(run, "first entry");
        await SeedLogAsync(run, "second entry");

        var page = _ctx.RenderComponent<LogsPage>();

        WaitForRow(page, "second entry");
        Messages(page).Should().Equal("second entry", "first entry");
        var query = LastQuery();
        query.Order.Should().Be(LogOrder.NewestFirst);
        query.MetadataId.Should().BeNull();
        _operations
            .CallsTo(nameof(IOperationsService.CountLogsCappedAsync))
            .Should()
            .NotBeEmpty("the pager's total comes from the service's count");
    }

    [Test]
    public async Task A_run_page_reads_its_own_log_oldest_first_through_the_service()
    {
        var run = await SeedRunAsync();
        var other = await SeedRunAsync();
        await SeedLogAsync(run, "first entry");
        await SeedLogAsync(other, "another run's entry");
        await SeedLogAsync(run, "second entry");

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, run));

        WaitForRow(page, "second entry");
        Messages(page).Should().Equal("first entry", "second entry");
        var query = LastQuery();
        query.Order.Should().Be(LogOrder.OldestFirst);
        query.MetadataId.Should().Be(run);
        page.FindAll(".cs-logs-run").Should().BeEmpty("the run's page is scoped to the run");
    }

    [Test]
    public async Task No_column_offers_a_filter_the_service_would_ignore()
    {
        var run = await SeedRunAsync();
        await SeedLogAsync(run, "an entry");

        var page = _ctx.RenderComponent<LogsPage>();

        WaitForRow(page, "an entry");
        page.FindAll(".rz-cell-filter").Should().BeEmpty();
        page.Find(".cs-logs-filter-hint").TextContent.Should().Contain("not searchable");
    }

    [Test]
    public async Task A_capped_count_is_said_above_the_grid()
    {
        var run = await SeedRunAsync();
        await SeedLogAsync(run, "an entry");
        // As the service counts: only a text filter is capped.
        _operations.Respond = (method, args) =>
            method == nameof(IOperationsService.CountLogsCappedAsync)
            && ((LogQuery)args[0]!).MessageContains is not null
                ? Task.FromResult(new LogCount(OperationsService.LogCountCap, Capped: true))
                : null;

        var page = _ctx.RenderComponent<LogsPage>();
        WaitForRow(page, "an entry");
        page.FindAll(".cs-logs-capped").Should().BeEmpty("an unfiltered count is exact");

        await page.Find("input.cs-logs-message").ChangeAsync(new() { Value = "entry" });

        page.WaitForAssertion(
            () => page.Find(".cs-logs-capped").TextContent.Should().Contain("More than 10,000"),
            WaitTimeout
        );
    }

    [Test]
    public async Task The_message_filter_is_sent_to_the_service()
    {
        var run = await SeedRunAsync();
        await SeedLogAsync(run, "Payment ACCEPTED");
        await SeedLogAsync(run, "payment refused");

        var page = _ctx.RenderComponent<LogsPage>();
        WaitForRow(page, "payment refused");

        await page.Find("input.cs-logs-message").ChangeAsync(new() { Value = "accepted" });

        page.WaitForAssertion(() => Messages(page).Should().Equal("Payment ACCEPTED"), WaitTimeout);
        LastQuery().MessageContains.Should().Be("accepted");
    }

    [Test]
    public async Task The_category_filter_is_sent_to_the_service()
    {
        var run = await SeedRunAsync();
        await SeedLogAsync(run, "from billing", category: "Acme.Billing.Invoices");
        await SeedLogAsync(run, "from shipping", category: "Acme.Shipping");

        var page = _ctx.RenderComponent<LogsPage>();
        WaitForRow(page, "from shipping");

        await page.Find("input.cs-logs-category").ChangeAsync(new() { Value = "billing" });

        page.WaitForAssertion(() => Messages(page).Should().Equal("from billing"), WaitTimeout);
        LastQuery().CategoryContains.Should().Be("billing");
    }

    [Test]
    public async Task The_minimum_level_is_sent_to_the_service()
    {
        var run = await SeedRunAsync();
        await SeedLogAsync(run, "an error", level: LogLevel.Error);
        await SeedLogAsync(run, "some information");

        var page = _ctx.RenderComponent<LogsPage>();
        WaitForRow(page, "some information");

        await page.Find(".cs-logs-level").ClickAsync(new());
        await page.FindAll(".rz-dropdown-item")
            .First(i => i.TextContent.Trim() == nameof(LogLevel.Warning))
            .ClickAsync(new());

        page.WaitForAssertion(() => Messages(page).Should().Equal("an error"), WaitTimeout);
        LastQuery().MinimumLevel.Should().Be(LogLevel.Warning);
    }

    [Test]
    public async Task The_run_filter_is_sent_to_the_service()
    {
        var run = await SeedRunAsync();
        var other = await SeedRunAsync();
        await SeedLogAsync(run, "wanted run");
        await SeedLogAsync(other, "other run");

        var page = _ctx.RenderComponent<LogsPage>();
        WaitForRow(page, "other run");

        await page.Find(".cs-logs-run input").ChangeAsync(new() { Value = run.ToString() });

        page.WaitForAssertion(() => Messages(page).Should().Equal("wanted run"), WaitTimeout);
        LastQuery().MetadataId.Should().Be(run);
    }

    [Test]
    public async Task Filters_on_the_run_page_stay_within_the_run()
    {
        var run = await SeedRunAsync();
        var other = await SeedRunAsync();
        await SeedLogAsync(run, "match in run");
        await SeedLogAsync(run, "other in run");
        await SeedLogAsync(other, "match elsewhere");

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, run));
        WaitForRow(page, "other in run");

        await page.Find("input.cs-logs-message").ChangeAsync(new() { Value = "match" });

        page.WaitForAssertion(() => Messages(page).Should().Equal("match in run"), WaitTimeout);
        LastQuery().MetadataId.Should().Be(run);
    }

    [Test]
    public async Task Pages_after_the_first_are_read_by_offset_from_the_service()
    {
        var run = await SeedRunAsync();
        for (var i = 0; i < 25; i++)
            await SeedLogAsync(run, $"entry {i:D2}");

        var page = _ctx.RenderComponent<LogsPage>();
        WaitForRow(page, "entry 24");

        await NextPage(page, "entry 04");

        Messages(page).Should().HaveCount(5);
        var query = LastQuery();
        query.Skip.Should().Be(20);
        query.Take.Should().Be(20);
    }

    private LogQuery LastQuery() =>
        (LogQuery)_operations.CallsTo(nameof(IOperationsService.GetLogsAsync)).Last()[0]!;

    // The message is the last column but one, the exception being the last.
    private static List<string> Messages(IRenderedFragment page) =>
        page.FindAll("tr.rz-data-row")
            .Select(r => r.QuerySelectorAll("td").ToList())
            .Select(cells => cells[^2].TextContent.Trim())
            .ToList();

    private async Task<long> SeedRunAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = "Acme.ILoggedTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }

    private async Task SeedLogAsync(
        long metadataId,
        string message,
        string category = "Acme.Logged",
        LogLevel level = LogLevel.Information
    )
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var log = Log.Create(
            new CreateLog
            {
                Level = level,
                CategoryName = category,
                Message = message,
                EventId = 0,
            }
        );
        log.Metadata = await db.Metadatas.SingleAsync(m => m.Id == metadataId);
        await db.Track(log);
        await db.SaveChanges(default);
    }
}
