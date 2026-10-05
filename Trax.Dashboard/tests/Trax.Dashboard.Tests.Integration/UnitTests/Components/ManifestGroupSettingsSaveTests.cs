using AwesomeAssertions;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.ManifestGroup;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The group detail page edits the group's settings in place and saves only the fields that
/// differ from the values it last loaded. Once a save succeeds, those values are the saved
/// ones: the form is clean again and later saves send only later edits.
/// </summary>
[TestFixture]
public class ManifestGroupSettingsSaveTests
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
        _ctx.Services.AddSingleton<IDataContextProviderFactory>(_data);
        _ctx.Services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        _ctx.Services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
        _ctx.Services.AddSingleton<IOperationsService>(
            new OperationsService(
                new TrainDiscoveryService(new ServiceCollection()),
                _data,
                new SchedulerConfiguration(),
                trainExecution: null!
            )
        );
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_second_save_does_not_resend_the_first_saves_fields()
    {
        var groupId = await SeedGroupAsync();
        var page = _ctx.RenderComponent<ManifestGroupDetailPage>(p =>
            p.Add(x => x.ManifestGroupId, groupId)
        );
        page.WaitForElement("input[name='GroupPriority']", TimeSpan.FromSeconds(10));

        // First edit: priority 0 -> 5, saved.
        page.Find("input[name='GroupPriority']").Change("5");
        await SaveButton(page).ClickAsync(new());
        (await PriorityAsync(groupId)).Should().Be(5, "the premise is a first save that succeeded");

        // Someone else (the API, another operator) then sets the priority to 10.
        await using (var db = await _data.CreateDbContextAsync(default))
        {
            var group = await db.ManifestGroups.SingleAsync(g => g.Id == groupId);
            group.Priority = 10;
            await db.SaveChanges(default);
        }

        // Second edit on this page touches only Enabled.
        await page.Find(".rz-switch").ClickAsync(new());
        await SaveButton(page).ClickAsync(new());

        (await IsEnabledAsync(groupId))
            .Should()
            .BeFalse("the premise is a second save that succeeded");
        (await PriorityAsync(groupId))
            .Should()
            .Be(
                10,
                "the second save changed only Enabled; resending the first save's priority "
                    + "overwrites a change made since"
            );
    }

    [Test]
    public async Task The_settings_are_clean_after_a_successful_save()
    {
        var groupId = await SeedGroupAsync();
        var page = _ctx.RenderComponent<ManifestGroupDetailPage>(p =>
            p.Add(x => x.ManifestGroupId, groupId)
        );
        page.WaitForElement("input[name='GroupPriority']", TimeSpan.FromSeconds(10));

        page.Find("input[name='GroupPriority']").Change("5");
        SaveButton(page).HasAttribute("disabled").Should().BeFalse("the premise is a dirty form");
        await SaveButton(page).ClickAsync(new());
        (await PriorityAsync(groupId)).Should().Be(5, "the premise is a first save that succeeded");

        page.WaitForAssertion(
            () =>
                SaveButton(page)
                    .HasAttribute("disabled")
                    .Should()
                    .BeTrue("the edit is saved, so there is nothing left to save"),
            TimeSpan.FromSeconds(5)
        );
    }

    private static AngleSharp.Dom.IElement SaveButton(IRenderedFragment page) =>
        page.FindAll("button").First(b => b.TextContent.Trim().EndsWith("Save"));

    private async Task<long> SeedGroupAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup
        {
            Name = "nightly",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.ManifestGroups.Add(group);
        await db.SaveChanges(default);
        return group.Id;
    }

    private async Task<int> PriorityAsync(long id)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return (await db.ManifestGroups.AsNoTracking().SingleAsync(g => g.Id == id)).Priority;
    }

    private async Task<bool> IsEnabledAsync(long id)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return (await db.ManifestGroups.AsNoTracking().SingleAsync(g => g.Id == id)).IsEnabled;
    }
}
