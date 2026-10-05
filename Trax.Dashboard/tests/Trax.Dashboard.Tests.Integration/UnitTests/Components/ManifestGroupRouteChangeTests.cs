using System.Reflection;
using AwesomeAssertions;
using Bunit;
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
/// Moving from one group's page to another's (a click on a node of the dependency graph) is a
/// route change on the same component. Everything on the page then belongs to the new group:
/// its name, its settings, and what Run Group and Save act on. Unsaved edits to the previous
/// group are dropped, not carried over.
/// </summary>
[TestFixture]
public class ManifestGroupRouteChangeTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private ITraxScheduler _scheduler = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _scheduler = TriggerRecordingScheduler.Create();
        _ctx.Services.AddSingleton<IDataContextProviderFactory>(_data);
        _ctx.Services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        _ctx.Services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        _ctx.Services.AddSingleton(_scheduler);
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
    public async Task After_a_route_change_the_page_shows_and_acts_on_the_new_group()
    {
        var groupA = await SeedGroupAsync("group-a", priority: 1);
        var groupB = await SeedGroupAsync("group-b", priority: 2);

        var page = _ctx.RenderComponent<ManifestGroupDetailPage>(p =>
            p.Add(x => x.ManifestGroupId, groupA)
        );
        page.WaitForElement("input[name='GroupPriority']", TimeSpan.FromSeconds(10));

        // An unsaved edit on A, then a click through to B.
        page.Find("input[name='GroupPriority']").Change("9");
        page.SetParametersAndRender(p => p.Add(x => x.ManifestGroupId, groupB));

        page.WaitForAssertion(
            () => page.Find("h4").TextContent.Should().Contain("group-b"),
            TimeSpan.FromSeconds(10)
        );
        page.Find("input[name='GroupPriority']")
            .GetAttribute("value")
            .Should()
            .Be("2", "the form shows B's settings, not A's unsaved edit");
        SaveButton(page)
            .HasAttribute("disabled")
            .Should()
            .BeTrue("the edit was to A, and is dropped when the page moves to B");

        await page.FindAll("button")
            .First(b => b.TextContent.Contains("Run Group"))
            .ClickAsync(new());

        ((TriggerRecordingScheduler)(object)_scheduler)
            .TriggeredGroups.Should()
            .Equal([groupB], "Run Group on B's page queues B");
    }

    private static AngleSharp.Dom.IElement SaveButton(IRenderedFragment page) =>
        page.FindAll("button").First(b => b.TextContent.Trim().EndsWith("Save"));

    private async Task<long> SeedGroupAsync(string name, int priority)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup
        {
            Name = name,
            Priority = priority,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.ManifestGroups.Add(group);
        await db.SaveChanges(default);
        return group.Id;
    }

    /// <summary>
    /// Records <c>TriggerGroupAsync</c> and reports no manifests queued; any other call fails the
    /// test.
    /// </summary>
    public class TriggerRecordingScheduler : DispatchProxy
    {
        public List<long> TriggeredGroups { get; } = [];

        public static ITraxScheduler Create() =>
            Create<ITraxScheduler, TriggerRecordingScheduler>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ITraxScheduler.TriggerGroupAsync))
            {
                TriggeredGroups.Add((long)args![0]!);
                return Task.FromResult(0);
            }

            throw new InvalidOperationException(
                $"ITraxScheduler.{targetMethod?.Name} was called, but this test does not expect it."
            );
        }
    }
}
