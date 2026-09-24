using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteManifestManagerTests : TestSetup
{
    private IManifestManagerTrain _train = null!;
    private SchedulerConfiguration _config = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        _config.StaleStagedEntryTimeout = TimeSpan.FromMinutes(10);
        _config.PromoteStaleStagedEntries = false;
    }

    [TearDown]
    public async Task ManifestManagerTestsTearDown()
    {
        _config.StaleStagedEntryTimeout = new SchedulerConfiguration().StaleStagedEntryTimeout;
        _config.PromoteStaleStagedEntries = false;

        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    #region Due Manifest Tests

    [Test]
    public async Task ManifestManager_DueManifest_CreatesWorkQueueEntry()
    {
        // Arrange — interval manifest that has never run is immediately due
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: true
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries.Should().HaveCount(1);
        entries[0].TrainName.Should().Be(typeof(SchedulerTestTrain).FullName);
    }

    [Test]
    public async Task ManifestManager_NotDueManifest_NoWorkQueueEntry()
    {
        // Arrange — interval manifest that just ran, not due yet
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            isEnabled: true
        );

        // Set LastSuccessfulRun to now so it won't be due for another hour
        manifest = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);
        manifest.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries.Should().BeEmpty("manifest is not due for another hour");
    }

    [Test]
    public async Task ManifestManager_DisabledManifest_NoWorkQueueEntry()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: false
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries.Should().BeEmpty("disabled manifests should not be queued");
    }

    [Test]
    public async Task ManifestManager_MultipleDueManifests_CreatesMultipleEntries()
    {
        // Arrange — three interval manifests that have never run
        var manifest1 = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: true,
            inputValue: "first"
        );
        var manifest2 = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: true,
            inputValue: "second"
        );
        var manifest3 = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: true,
            inputValue: "third"
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var entries = await DataContext.WorkQueues.ToListAsync();

        var manifestIds = new List<long> { manifest1.Id, manifest2.Id, manifest3.Id };
        var matchingEntries = entries
            .Where(e => e.ManifestId.HasValue && manifestIds.Contains(e.ManifestId.Value))
            .ToList();

        matchingEntries.Should().HaveCount(3);
    }

    #endregion

    #region Stale Staged Entry Tests

    // SQLite stores DateTime as TEXT, so the sweep's CreatedAt < cutoff is a string comparison.
    // These run actual staged rows through the ManifestManager, a day past the cutoff and a
    // minute either side of it, so a stored format that stopped sorting like the instant it
    // encodes would put an entry on the wrong side.

    [Test]
    public async Task ManifestManager_StaleStagedEntries_AreCancelledAndRecentOnesLeftAlone()
    {
        var daysOld = await Stage(age: TimeSpan.FromDays(1));
        var justPastTimeout = await Stage(age: TimeSpan.FromMinutes(11));
        var justInsideTimeout = await Stage(age: TimeSpan.FromMinutes(9));
        var confirmed = await Stage(age: TimeSpan.FromDays(1), confirm: true);

        await _train.Run(Unit.Default);

        (await Load(daysOld)).Status.Should().Be(WorkQueueStatus.Cancelled);
        (await Load(justPastTimeout)).Status.Should().Be(WorkQueueStatus.Cancelled);

        var recent = await Load(justInsideTimeout);
        recent.Status.Should().Be(WorkQueueStatus.Queued, "its hook may still be running");
        recent.ConfirmedAt.Should().BeNull();

        (await Load(confirmed))
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "only unconfirmed entries are stranded");
    }

    [Test]
    public async Task ManifestManager_StaleStagedEntry_IsPromotedWhenTheHostOptsIn()
    {
        _config.PromoteStaleStagedEntries = true;
        var stale = await Stage(age: TimeSpan.FromMinutes(11));
        var recent = await Stage(age: TimeSpan.FromMinutes(9));

        await _train.Run(Unit.Default);

        var promoted = await Load(stale);
        promoted.Status.Should().Be(WorkQueueStatus.Queued);
        promoted.ConfirmedAt.Should().NotBeNull();
        (await Load(recent)).ConfirmedAt.Should().BeNull();
    }

    private async Task<long> Stage(TimeSpan age, bool confirm = false)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = "{}",
                InputTypeName = typeof(SchedulerTestInput).FullName,
                DeferPromotion = !confirm,
            }
        );
        entry.CreatedAt = DateTime.UtcNow - age;

        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return entry.Id;
    }

    private async Task<WorkQueue> Load(long id)
    {
        DataContext.Reset();
        return await DataContext.WorkQueues.AsNoTracking().FirstAsync(w => w.Id == id);
    }

    #endregion

    #region Helper Methods

    private async Task<Manifest> CreateAndSaveManifest(
        ScheduleType scheduleType = ScheduleType.None,
        int? intervalSeconds = null,
        bool isEnabled = true,
        string inputValue = "TestValue"
    )
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = isEnabled,
                ScheduleType = scheduleType,
                IntervalSeconds = intervalSeconds,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = inputValue },
            }
        );

        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    #endregion
}
