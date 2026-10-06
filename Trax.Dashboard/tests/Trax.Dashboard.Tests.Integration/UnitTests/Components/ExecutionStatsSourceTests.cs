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
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The group and manifest run counts on the dashboard come from the operations service calls
/// the API's stats queries make, not from a copy of their queries, so the two surfaces cannot
/// drift. The group list asks in batches the service accepts, however many groups there are.
/// Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md states the
/// principle.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class ExecutionStatsSourceTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private RecordingOperations _operations = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();

        var operations = RecordingOperations.Wrap(
            new OperationsService(
                new TrainDiscoveryService(new ServiceCollection()),
                _data,
                new SchedulerConfiguration(),
                trainExecution: null!
            )
        );
        _operations = (RecordingOperations)(object)operations;

        var services = _ctx.Services;
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        services.AddSingleton(UnusedService<ITraxScheduler>.Create());
        services.AddSingleton(operations);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task The_group_list_asks_the_service_in_batches_it_accepts()
    {
        var groupIds = new List<long>();
        await using (var db = await _data.CreateDbContextAsync(default))
        {
            var groups = Enumerable
                .Range(0, OperationsService.MaxBatchSize + 1)
                .Select(i => new ManifestGroup { Name = $"group-{i:D4}" })
                .ToList();
            foreach (var group in groups)
                await db.Track(group);
            await db.SaveChanges(default);
            groupIds.AddRange(groups.Select(g => g.Id));
        }

        var page = _ctx.RenderComponent<ManifestGroupsPage>();

        page.WaitForAssertion(
            () => page.FindAll("tr.rz-data-row").Should().NotBeEmpty(),
            TimeSpan.FromSeconds(20)
        );
        var batches = _operations
            .GroupStatsBatches.Should()
            .NotBeEmpty("the counts come from GetManifestGroupExecutionStatsAsync")
            .And.Subject;
        batches
            .Should()
            .OnlyContain(
                b => b.Count <= OperationsService.MaxBatchSize,
                "the service refuses a larger batch"
            );
        batches
            .Take(2)
            .SelectMany(b => b)
            .Should()
            .BeEquivalentTo(groupIds, "every listed group gets its counts");
    }

    [Test]
    public async Task The_group_page_counts_come_from_the_service()
    {
        var (groupId, _) = await SeedManifestWithRunsAsync();

        var page = _ctx.RenderComponent<ManifestGroupDetailPage>(p =>
            p.Add(x => x.ManifestGroupId, groupId)
        );

        page.WaitForAssertion(
            () =>
                _operations
                    .GroupStatsBatches.Should()
                    .Contain(b => b.SequenceEqual(new[] { groupId })),
            TimeSpan.FromSeconds(10)
        );
        page.WaitForAssertion(
            () => CardValue(page, "Completed").Should().Be("2"),
            TimeSpan.FromSeconds(10)
        );
    }

    [Test]
    public async Task The_manifest_page_counts_come_from_the_service()
    {
        var (_, manifestId) = await SeedManifestWithRunsAsync();

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );

        page.WaitForAssertion(
            () => _operations.ManifestStatsIds.Should().Contain(manifestId),
            TimeSpan.FromSeconds(10)
        );
    }

    private static string CardValue<T>(IRenderedComponent<T> page, string caption)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        page.FindAll(".rz-card")
            .Where(c =>
                c.QuerySelectorAll(".rz-text-caption").Any(t => t.TextContent.Trim() == caption)
            )
            .Select(c => c.QuerySelector(".rz-text-h3")!.TextContent.Trim())
            .First();

    private async Task<(long GroupId, long ManifestId)> SeedManifestWithRunsAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = "counted" };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(ICountedTrain) });
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);

        foreach (
            var state in new[] { TrainState.Completed, TrainState.Completed, TrainState.Failed }
        )
        {
            var run = Metadata.Create(
                new CreateMetadata
                {
                    Name = typeof(ICountedTrain).FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                    ManifestId = manifest.Id,
                }
            );
            run.TrainState = state;
            await db.Track(run);
        }
        await db.SaveChanges(default);
        return (group.Id, manifest.Id);
    }

    public interface ICountedTrain { }

    /// <summary>
    /// Forwards every call to the real operations service and records the stats calls.
    /// </summary>
    public class RecordingOperations : DispatchProxy
    {
        private IOperationsService _inner = null!;

        public List<IReadOnlyList<long>> GroupStatsBatches { get; } = [];

        public List<long> ManifestStatsIds { get; } = [];

        public static IOperationsService Wrap(IOperationsService inner)
        {
            var proxy = Create<IOperationsService, RecordingOperations>();
            ((RecordingOperations)(object)proxy)._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IOperationsService.GetManifestGroupExecutionStatsAsync):
                    lock (GroupStatsBatches)
                        GroupStatsBatches.Add(((IEnumerable<long>)args![0]!).ToList());
                    break;
                case nameof(IOperationsService.GetManifestExecutionStatsAsync):
                    lock (ManifestStatsIds)
                        ManifestStatsIds.Add((long)args![0]!);
                    break;
            }

            try
            {
                return targetMethod!.Invoke(_inner, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw e.InnerException;
            }
        }
    }
}
