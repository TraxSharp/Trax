using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteManifestManagerTests : TestSetup
{
    private IManifestManagerTrain _train = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
    }

    [TearDown]
    public async Task ManifestManagerTestsTearDown()
    {
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
