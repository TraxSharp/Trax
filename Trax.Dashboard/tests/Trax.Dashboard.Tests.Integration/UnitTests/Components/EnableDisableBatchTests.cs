using Bunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// Enabling and disabling manifests and manifest groups goes through the operations service's
/// batch methods, the calls the API's <c>setManifestsEnabled</c>, <c>setManifestGroupsEnabled</c>
/// and <c>setAllManifestGroupsEnabled</c> make, and the page reports the service's count: only
/// the rows whose flag changed.
/// Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md states the
/// principle.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class EnableDisableBatchTests
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
    public async Task Disable_selected_manifests_reports_only_the_ones_that_changed()
    {
        var on = await SeedManifestAsync("group-on", "manifest-on", enabled: true);
        var off = await SeedManifestAsync("group-off", "manifest-off", enabled: false);

        var page = _ctx.RenderComponent<ManifestsPage>();
        foreach (var group in new[] { "group-on", "group-off" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }

        await ClickButton(page, "Disable Selected");

        page.WaitForAssertion(
            () => Messages().Should().Contain("1 of 2 manifest(s) disabled."),
            WaitTimeout
        );
        (await ManifestEnabledAsync(on)).Should().BeFalse();
        (await ManifestEnabledAsync(off)).Should().BeFalse();
    }

    [Test]
    public async Task Disable_selected_groups_reports_only_the_ones_that_changed()
    {
        await SeedManifestAsync("group-on", "a", enabled: true);
        await SeedManifestAsync("group-off", "b", enabled: true, groupEnabled: false);

        var page = _ctx.RenderComponent<ManifestGroupsPage>();
        foreach (var group in new[] { "group-on", "group-off" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }

        await ClickButton(page, "Disable Selected");

        page.WaitForAssertion(
            () => Messages().Should().Contain("1 of 2 manifest group(s) disabled."),
            WaitTimeout
        );
        (await GroupsEnabledAsync()).Should().AllBeEquivalentTo(false);
    }

    [Test]
    public async Task Disable_all_groups_disables_every_group_through_the_service()
    {
        await SeedManifestAsync("group-a", "a", enabled: true);
        await SeedManifestAsync("group-b", "b", enabled: true);

        var page = _ctx.RenderComponent<ManifestGroupsPage>();
        WaitForRow(page, "group-b");

        await ClickButton(page, "Disable All");

        page.WaitForAssertion(
            () => Messages().Should().Contain("2 manifest group(s) disabled."),
            WaitTimeout
        );
        (await GroupsEnabledAsync()).Should().AllBeEquivalentTo(false);
    }

    private IEnumerable<string> Messages() =>
        _ctx.Services.GetRequiredService<NotificationService>().Messages.Select(m => m.Detail);

    private async Task<long> SeedManifestAsync(
        string groupName,
        string externalId,
        bool enabled,
        bool groupEnabled = true
    )
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = groupName, IsEnabled = groupEnabled };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IBatchTrain) });
        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = group.Id;
        manifest.IsEnabled = enabled;
        await db.Track(manifest);
        await db.SaveChanges(default);
        return manifest.Id;
    }

    private async Task<bool> ManifestEnabledAsync(long id)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return (await db.Manifests.AsNoTracking().SingleAsync(m => m.Id == id)).IsEnabled;
    }

    private async Task<List<bool>> GroupsEnabledAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return await db.ManifestGroups.AsNoTracking().Select(g => g.IsEnabled).ToListAsync();
    }

    public interface IBatchTrain { }
}
