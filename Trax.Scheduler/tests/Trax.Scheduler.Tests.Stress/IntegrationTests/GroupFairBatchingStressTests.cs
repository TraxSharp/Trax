using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.ManifestGroup;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Stress.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Stress.IntegrationTests;

/// <summary>
/// Stress tests for group-fair batching in CreateWorkQueueEntriesJunction.
/// Validates that the fairness algorithm works correctly and efficiently
/// at scale, reproducing the SuiteMirror scenario where thousands of
/// manifests in one group starved smaller groups from being queued.
///
/// Run with: dotnet test --filter "TestCategory=Stress"
/// </summary>
[TestFixture]
[Category("Stress")]
public class GroupFairBatchingStressTests : TestSetup
{
    private IManifestManagerTrain _train = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
    }

    [TearDown]
    public void GroupFairBatchingTearDown()
    {
        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    [Test]
    public async Task GroupFairBatching_SuiteMirrorScenario_DeltasGetQueued()
    {
        // Arrange — reproduces the exact SuiteMirror bug: 4425 cache manifests
        // in one group, 15 delta manifests in another. Without group-fair batching,
        // the delta group is perpetually starved by the flat Take(200).
        var cacheGroup = await SeedManifestGroup("cache-import");
        var deltaGroup = await SeedManifestGroup("delta");

        await SeedManifests(4425, cacheGroup.Id);
        await SeedManifests(15, deltaGroup.Id);

        var config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalLimit = config.MaxWorkQueueEntriesPerCycle;
        config.MaxWorkQueueEntriesPerCycle = 200;

        try
        {
            // Act
            await _train.Run(Unit.Default);

            // Assert
            DataContext.Reset();
            var deltaEntries = await DataContext
                .WorkQueues.Include(q => q.Manifest)
                .Where(q =>
                    q.Status == WorkQueueStatus.Queued
                    && q.Manifest!.ManifestGroupId == deltaGroup.Id
                )
                .CountAsync();

            var totalEntries = await DataContext
                .WorkQueues.Where(q => q.Status == WorkQueueStatus.Queued)
                .CountAsync();

            deltaEntries
                .Should()
                .Be(
                    15,
                    "delta group should get ALL its due manifests queued (15 < base allocation)"
                );
            totalEntries.Should().Be(200, "total entries should equal the per-cycle limit");
        }
        finally
        {
            config.MaxWorkQueueEntriesPerCycle = originalLimit;
        }
    }

    [Test]
    public async Task GroupFairBatching_HighManifestCount_PerformanceAcceptable()
    {
        // Arrange — 10 groups × 1000 manifests = 10,000 manifests
        var groups = new List<ManifestGroup>();
        for (var g = 0; g < 10; g++)
        {
            var group = await SeedManifestGroup($"perf-group-{g}");
            groups.Add(group);
            await SeedManifests(1000, group.Id);
        }

        var config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalLimit = config.MaxWorkQueueEntriesPerCycle;
        config.MaxWorkQueueEntriesPerCycle = 500;

        try
        {
            // Act — must complete within 5 seconds
            await AssertCompletesWithin(async () => await _train.Run(Unit.Default));

            // Assert — each group gets exactly 50 entries (500 / 10)
            DataContext.Reset();
            foreach (var group in groups)
            {
                var count = await DataContext
                    .WorkQueues.Include(q => q.Manifest)
                    .Where(q =>
                        q.Status == WorkQueueStatus.Queued
                        && q.Manifest!.ManifestGroupId == group.Id
                    )
                    .CountAsync();

                count.Should().Be(50, $"group '{group.Name}' should get limit/numGroups entries");
            }
        }
        finally
        {
            config.MaxWorkQueueEntriesPerCycle = originalLimit;
        }
    }

    [Test]
    public async Task GroupFairBatching_ManySmallGroups_AllRepresented()
    {
        // Arrange — 50 groups × 10 manifests = 500 manifests. Limit = 200.
        // Each group should get at least some entries (200 / 50 = 4 per group).
        var groups = new List<ManifestGroup>();
        for (var g = 0; g < 50; g++)
        {
            var group = await SeedManifestGroup($"small-group-{g}");
            groups.Add(group);
            await SeedManifests(10, group.Id);
        }

        var config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalLimit = config.MaxWorkQueueEntriesPerCycle;
        config.MaxWorkQueueEntriesPerCycle = 200;

        try
        {
            // Act
            await _train.Run(Unit.Default);

            // Assert — each group should get exactly 4 entries (200 / 50)
            DataContext.Reset();
            foreach (var group in groups)
            {
                var count = await DataContext
                    .WorkQueues.Include(q => q.Manifest)
                    .Where(q =>
                        q.Status == WorkQueueStatus.Queued
                        && q.Manifest!.ManifestGroupId == group.Id
                    )
                    .CountAsync();

                count
                    .Should()
                    .Be(4, $"group '{group.Name}' should get limit/numGroups = 4 entries");
            }

            var total = await DataContext
                .WorkQueues.Where(q => q.Status == WorkQueueStatus.Queued)
                .CountAsync();
            total.Should().Be(200, "total entries should equal the limit");
        }
        finally
        {
            config.MaxWorkQueueEntriesPerCycle = originalLimit;
        }
    }
}
