using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The work queue page's batch cancel acts on the rows the operator has ticked. The grid is
/// server-paged, so every page change loads new row objects; the selection has to follow the
/// entry, not the object that happened to be on screen when it was ticked.
/// </summary>
[TestFixture]
public class WorkQueueSelectionTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_row_unticked_after_a_page_change_leaves_nothing_selected()
    {
        // 25 entries: the page shows the newest 20, so "subject-25" is on page 1.
        for (var i = 1; i <= 25; i++)
            await SeedAsync($"subject-{i:00}");

        var page = _ctx.RenderComponent<WorkQueuePage>();
        WaitForRow(page, "subject-25");

        // Tick it, page away and back.
        await ToggleRow(page, "subject-25");
        page.WaitForAssertion(() => ButtonText(page, "Cancel Selected").Should().Contain("(1)"));

        await NextPage(page, "subject-05");
        await PreviousPage(page, "subject-25");

        // The operator unticks the entry: if the row shows unticked, by ticking and unticking it.
        // Either way nothing on screen is selected afterwards.
        if (!IsTicked(page, "subject-25"))
            await ToggleRow(page, "subject-25");
        await ToggleRow(page, "subject-25");
        IsTicked(page, "subject-25").Should().BeFalse("the premise is a row shown unticked");

        // Cancel Selected cancels by the ids in the selection, so what it offers is what it
        // would cancel.
        ButtonText(page, "Cancel Selected")
            .Should()
            .BeEmpty(
                "no row is ticked, so the page must not offer to cancel a selection; it still "
                    + "holds the entry ticked before the page change, which the operator unticked"
            );
    }

    [Test]
    public async Task A_row_ticked_before_a_page_change_still_shows_ticked_after_it()
    {
        for (var i = 1; i <= 25; i++)
            await SeedAsync($"subject-{i:00}");

        var page = _ctx.RenderComponent<WorkQueuePage>();
        WaitForRow(page, "subject-25");

        await ToggleRow(page, "subject-25");
        await NextPage(page, "subject-05");
        await PreviousPage(page, "subject-25");

        IsTicked(page, "subject-25")
            .Should()
            .BeTrue("the entry is still selected, so its row must say so");
        ButtonText(page, "Cancel Selected").Should().Contain("(1)");
    }

    private async Task SeedAsync(string subject)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = subject });
        db.WorkQueues.Add(entry);
        await db.SaveChanges(default);
    }
}
