using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// E2E test verifying that the ManifestManager's group-fair batching prevents
/// large manifest groups from starving smaller groups when MaxWorkQueueEntriesPerCycle
/// limits the number of entries created per cycle.
/// </summary>
[TestFixture]
public class GroupFairBatchingE2ETests : SchedulerTestFixture
{
    [Test]
    [Category("RequiresSchedulerFix")]
    [Ignore("Requires Trax.Scheduler with group-fair batching (TraxSharp/Trax.Scheduler#44)")]
    public async Task ManifestManager_GroupFairBatching_SmallGroupNotStarved()
    {
        // Arrange — disable ALL pre-seeded GameServer manifest groups so only our
        // test groups are active. This isolates the test from the sample topology.
        var preSeededGroupIds = await DataContext.ManifestGroups.Select(g => g.Id).ToListAsync();

        await DataContext
            .ManifestGroups.Where(g => preSeededGroupIds.Contains(g.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsEnabled, false));
        DataContext.Reset();

        var config = GetSchedulerConfiguration();
        var originalLimit = config.MaxWorkQueueEntriesPerCycle;
        config.MaxWorkQueueEntriesPerCycle = 10;

        var largeGroup = new ManifestGroup
        {
            Name = "e2e-large-group",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await DataContext.Track(largeGroup);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var smallGroup = new ManifestGroup
        {
            Name = "e2e-small-group",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await DataContext.Track(smallGroup);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var trainType = typeof(IRecalculateLeaderboardTrain);

        for (var i = 0; i < 30; i++)
        {
            var manifest = Manifest.Create(
                new CreateManifest
                {
                    Name = trainType,
                    IsEnabled = true,
                    ScheduleType = ScheduleType.Interval,
                    IntervalSeconds = 1,
                    MaxRetries = 1,
                    Properties = new RecalculateLeaderboardInput { Region = "test" },
                }
            );
            manifest.ManifestGroupId = largeGroup.Id;
            manifest.ExternalId = $"e2e-fair-large-{i}";
            await DataContext.Track(manifest);
        }

        for (var i = 0; i < 3; i++)
        {
            var manifest = Manifest.Create(
                new CreateManifest
                {
                    Name = trainType,
                    IsEnabled = true,
                    ScheduleType = ScheduleType.Interval,
                    IntervalSeconds = 1,
                    MaxRetries = 1,
                    Properties = new RecalculateLeaderboardInput { Region = "test" },
                }
            );
            manifest.ManifestGroupId = smallGroup.Id;
            manifest.ExternalId = $"e2e-fair-small-{i}";
            await DataContext.Track(manifest);
        }

        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        try
        {
            // Act — invoke the ManifestManagerTrain directly (deterministic, no timing issues)
            using var trainScope = SharedSchedulerSetup.Factory.Services.CreateScope();
            var train = trainScope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
            await train.Run(Unit.Default);

            // Assert — use a fresh DataContext to avoid stale EF tracking
            using var assertScope = SharedSchedulerSetup.Factory.Services.CreateScope();
            var assertDcFactory =
                assertScope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
            var assertDc = (IDataContext)assertDcFactory.Create();

            var smallGroupEntries = await assertDc
                .WorkQueues.Include(q => q.Manifest)
                .Where(q =>
                    q.Status == WorkQueueStatus.Queued
                    && q.Manifest != null
                    && q.Manifest.ManifestGroupId == smallGroup.Id
                )
                .CountAsync();

            var largeGroupEntries = await assertDc
                .WorkQueues.Include(q => q.Manifest)
                .Where(q =>
                    q.Status == WorkQueueStatus.Queued
                    && q.Manifest != null
                    && q.Manifest.ManifestGroupId == largeGroup.Id
                )
                .CountAsync();

            smallGroupEntries
                .Should()
                .Be(3, "small group should get all its due manifests queued (3 < base allocation)");
            largeGroupEntries
                .Should()
                .Be(7, "large group should get the remaining budget (10 - 3)");

            trainScope.Dispose();
        }
        finally
        {
            config.MaxWorkQueueEntriesPerCycle = originalLimit;

            // Clean up test data in FK-safe order (children before parents)
            var testManifestIds = await DataContext
                .Manifests.Where(m =>
                    m.ManifestGroupId == largeGroup.Id || m.ManifestGroupId == smallGroup.Id
                )
                .Select(m => m.Id)
                .ToListAsync();

            await DataContext
                .WorkQueues.Where(q =>
                    q.ManifestId.HasValue && testManifestIds.Contains(q.ManifestId.Value)
                )
                .ExecuteDeleteAsync();
            await DataContext
                .Manifests.Where(m => testManifestIds.Contains(m.Id))
                .ExecuteDeleteAsync();
            await DataContext
                .ManifestGroups.Where(g => g.Id == largeGroup.Id || g.Id == smallGroup.Id)
                .ExecuteDeleteAsync();

            // Re-enable pre-seeded groups
            await DataContext
                .ManifestGroups.Where(g => preSeededGroupIds.Contains(g.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsEnabled, true));
        }
    }
}
