using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The runs list's batch cancel acts on the runs the operator has ticked, followed by id across
/// the server-paged grid's page changes.
/// </summary>
[TestFixture]
public class MetadataSelectionTests
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
        for (var i = 1; i <= 25; i++)
            await SeedRunAsync($"Acme.IJob{i:00}", TrainState.InProgress);

        var page = _ctx.RenderComponent<MetadataPage>();
        WaitForRow(page, "IJob25");

        await ToggleRow(page, "IJob25");
        page.WaitForAssertion(() => ButtonText(page, "Cancel Selected").Should().Contain("(1)"));

        await NextPage(page, "IJob05");
        await PreviousPage(page, "IJob25");

        if (!IsTicked(page, "IJob25"))
            await ToggleRow(page, "IJob25");
        await ToggleRow(page, "IJob25");
        IsTicked(page, "IJob25").Should().BeFalse("the premise is a row shown unticked");

        ButtonText(page, "Cancel Selected")
            .Should()
            .BeEmpty("no run is ticked, so there is no selection to cancel");
    }

    private async Task<long> SeedRunAsync(string name, TrainState state)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.TrainState = state;
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }
}
