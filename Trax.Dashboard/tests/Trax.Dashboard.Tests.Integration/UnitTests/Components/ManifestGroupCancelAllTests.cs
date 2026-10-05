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
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The manifest group page's "Cancel All Running" goes through
/// <see cref="ITraxScheduler.CancelGroupAsync"/>, the scheduler's own group cancel, rather
/// than a copy of its query. Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md
/// states the principle.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class ManifestGroupCancelAllTests
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
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Cancel_all_running_cancels_the_group_through_the_scheduler()
    {
        var groupId = await SeedGroupWithRunningRunAsync();
        var scheduler = RecordingScheduler.Create();

        var discovery = new TrainDiscoveryService(new ServiceCollection());
        var services = _ctx.Services;
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        services.AddSingleton(scheduler);
        services.AddScoped<ITrainExecutionService>(sp => new TrainExecutionService(
            discovery,
            runExecutor: null!,
            concurrencyLimiter: null!,
            _data,
            new MediatorConfiguration(),
            sp
        ));
        services.AddScoped<IOperationsService>(sp => new OperationsService(
            discovery,
            _data,
            new SchedulerConfiguration(),
            sp.GetRequiredService<ITrainExecutionService>()
        ));

        var page = _ctx.RenderComponent<ManifestGroupDetailPage>(p =>
            p.Add(x => x.ManifestGroupId, groupId)
        );

        var button = page.WaitForElement(
            "button:contains('Cancel All Running')",
            TimeSpan.FromSeconds(10)
        );
        await button.ClickAsync(new());

        ((RecordingScheduler)(object)scheduler)
            .CancelledGroups.Should()
            .Equal([groupId], "the page cancels through the scheduler's CancelGroupAsync");
        _ctx.Services.GetRequiredService<NotificationService>()
            .Messages.Select(m => m.Detail)
            .Should()
            .Contain("Cancel signal sent for 1 train(s).");
    }

    private async Task<long> SeedGroupWithRunningRunAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = "cancel-group" };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IGroupedTrain) });
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);

        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IGroupedTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = TrainState.InProgress;
        await db.Track(run);
        await db.SaveChanges(default);
        return group.Id;
    }

    public interface IGroupedTrain { }

    /// <summary>
    /// Records <c>CancelGroupAsync</c> and reports one execution cancelled; any other call
    /// fails the test.
    /// </summary>
    public class RecordingScheduler : DispatchProxy
    {
        public List<long> CancelledGroups { get; } = [];

        public static ITraxScheduler Create() => Create<ITraxScheduler, RecordingScheduler>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ITraxScheduler.CancelGroupAsync))
            {
                CancelledGroups.Add((long)args![0]!);
                return Task.FromResult(1);
            }

            throw new InvalidOperationException(
                $"ITraxScheduler.{targetMethod?.Name} was called, but this test does not expect it."
            );
        }
    }
}
