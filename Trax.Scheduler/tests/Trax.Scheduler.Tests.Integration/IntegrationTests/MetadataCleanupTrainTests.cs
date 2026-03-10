using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the MetadataCleanupTrain which deletes expired metadata
/// entries for whitelisted train types.
/// </summary>
[TestFixture]
public class MetadataCleanupTrainTests : TestSetup
{
    private IMetadataCleanupTrain _train = null!;
    private SchedulerConfiguration _config = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
    }

    [TearDown]
    public async Task MetadataCleanupTrainTestsTearDown()
    {
        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    #region Default Configuration Tests

    [Test]
    public void DefaultWhitelist_ContainsManifestManagerTrain()
    {
        _config
            .MetadataCleanup!.TrainTypeWhitelist.Should()
            .Contain(
                typeof(ManifestManagerTrain).FullName!,
                "ManifestManagerTrain should be in the default whitelist"
            );
    }

    [Test]
    public void DefaultWhitelist_ContainsMetadataCleanupTrain()
    {
        _config
            .MetadataCleanup!.TrainTypeWhitelist.Should()
            .Contain(
                typeof(MetadataCleanupTrain).FullName!,
                "MetadataCleanupTrain should be in the default whitelist"
            );
    }

    [Test]
    public void DefaultRetentionPeriod_IsThirtyMinutes()
    {
        _config
            .MetadataCleanup!.RetentionPeriod.Should()
            .Be(TimeSpan.FromMinutes(30), "default retention period should be 30 minutes");
    }

    [Test]
    public void DefaultCleanupInterval_IsOneMinute()
    {
        _config
            .MetadataCleanup!.CleanupInterval.Should()
            .Be(TimeSpan.FromMinutes(1), "default cleanup interval should be 1 minute");
    }

    #endregion

    #region Deletion Tests - Terminal States

    [Test]
    public async Task Run_DeletesExpiredCompletedMetadata()
    {
        // Arrange - Create completed metadata older than retention period
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining.Should().BeNull("expired completed metadata should be deleted");
    }

    [Test]
    public async Task Run_DeletesExpiredFailedMetadata()
    {
        // Arrange - Create failed metadata older than retention period
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining.Should().BeNull("expired failed metadata should be deleted");
    }

    #endregion

    #region Retention Period Tests

    [Test]
    public async Task Run_DoesNotDeleteRecentMetadata()
    {
        // Arrange - Create completed metadata within retention period
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-15) // 15 min ago, within 30 minute retention
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining
            .Should()
            .NotBeNull("recent metadata within retention period should not be deleted");
    }

    #endregion

    #region Whitelist Filtering Tests

    [Test]
    public async Task Run_DoesNotDeleteNonWhitelistedMetadata()
    {
        // Arrange - Create old completed metadata for a non-whitelisted train
        var metadata = await CreateAndSaveMetadata(
            name: "SomeOtherTrain",
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining.Should().NotBeNull("metadata for non-whitelisted trains should not be deleted");
    }

    [Test]
    public async Task Run_DeletesMetadataForAllWhitelistedTypes()
    {
        // Arrange - Create expired metadata for both default whitelisted types
        var managerMetadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var cleanupMetadata = await CreateAndSaveMetadata(
            name: typeof(MetadataCleanupTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var managerRemaining = await DataContext
            .Metadatas.Where(m => m.Id == managerMetadata.Id)
            .FirstOrDefaultAsync();
        var cleanupRemaining = await DataContext
            .Metadatas.Where(m => m.Id == cleanupMetadata.Id)
            .FirstOrDefaultAsync();

        managerRemaining.Should().BeNull("expired ManifestManagerTrain metadata should be deleted");
        cleanupRemaining.Should().BeNull("expired MetadataCleanupTrain metadata should be deleted");
    }

    #endregion

    #region Non-Terminal State Tests

    [Test]
    public async Task Run_DoesNotDeletePendingMetadata()
    {
        // Arrange - Create old pending metadata (non-terminal state)
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Pending,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining.Should().NotBeNull("pending metadata should never be deleted regardless of age");
    }

    [Test]
    public async Task Run_DoesNotDeleteInProgressMetadata()
    {
        // Arrange - Create old in-progress metadata (non-terminal state)
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.InProgress,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining
            .Should()
            .NotBeNull("in-progress metadata should never be deleted regardless of age");
    }

    #endregion

    #region Associated Logs Tests

    [Test]
    public async Task Run_DeletesAssociatedLogs()
    {
        // Arrange - Create expired metadata with associated logs
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var log = Log.Create(
            new CreateLog
            {
                Level = LogLevel.Information,
                Message = "Test log entry",
                CategoryName = "TestCategory",
                EventId = 1,
            }
        );

        // Set MetadataId by tracking the log in the context
        await DataContext.Logs.AddAsync(log);

        // Use raw SQL to set the metadata_id since MetadataId has a private setter
        await DataContext.SaveChanges(CancellationToken.None);
        var logId = log.Id;

        await DataContext
            .Logs.Where(l => l.Id == logId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.MetadataId, metadata.Id));

        DataContext.Reset();

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remainingLog = await DataContext.Logs.Where(l => l.Id == logId).FirstOrDefaultAsync();
        var remainingMetadata = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remainingLog
            .Should()
            .BeNull("logs associated with deleted metadata should also be deleted");
        remainingMetadata.Should().BeNull("the metadata itself should be deleted");
    }

    #endregion

    #region Associated Work Queue Tests

    [Test]
    public async Task Run_DeletesAssociatedWorkQueueEntries()
    {
        // Arrange - Create expired metadata with an associated dispatched work queue entry
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var workQueueEntry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ManifestManagerTrain).FullName!,
                Input = null,
                InputTypeName = null,
            }
        );

        await DataContext.Track(workQueueEntry);
        await DataContext.SaveChanges(CancellationToken.None);
        var entryId = workQueueEntry.Id;

        // Link the work queue entry to the metadata and mark as dispatched
        await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(wq => wq.MetadataId, metadata.Id)
                    .SetProperty(wq => wq.Status, WorkQueueStatus.Dispatched)
                    .SetProperty(wq => wq.DispatchedAt, DateTime.UtcNow)
            );

        DataContext.Reset();

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remainingEntry = await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .FirstOrDefaultAsync();
        var remainingMetadata = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remainingEntry
            .Should()
            .BeNull("work queue entries associated with deleted metadata should also be deleted");
        remainingMetadata.Should().BeNull("the metadata itself should be deleted");
    }

    [Test]
    public async Task Run_DoesNotDeleteWorkQueueEntriesForNonExpiredMetadata()
    {
        // Arrange - Create recent metadata with an associated work queue entry
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-10) // Within 30 minute retention
        );

        var workQueueEntry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ManifestManagerTrain).FullName!,
                Input = null,
                InputTypeName = null,
            }
        );

        await DataContext.Track(workQueueEntry);
        await DataContext.SaveChanges(CancellationToken.None);
        var entryId = workQueueEntry.Id;

        await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(wq => wq.MetadataId, metadata.Id)
                    .SetProperty(wq => wq.Status, WorkQueueStatus.Dispatched)
                    .SetProperty(wq => wq.DispatchedAt, DateTime.UtcNow)
            );

        DataContext.Reset();

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remainingEntry = await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .FirstOrDefaultAsync();

        remainingEntry
            .Should()
            .NotBeNull("work queue entries for non-expired metadata should survive cleanup");
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Run_WithNoExpiredMetadata_CompletesSuccessfully()
    {
        // Act & Assert - Should complete without throwing
        var act = async () => await _train.Run(new MetadataCleanupRequest());
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_WithMixOfEligibleAndIneligibleMetadata_DeletesOnlyEligible()
    {
        // Arrange
        var expiredWhitelisted = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var recentWhitelisted = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-10)
        );

        var expiredNonWhitelisted = await CreateAndSaveMetadata(
            name: "SomeOtherTrain",
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var expiredPending = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Pending,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();

        var deletedCheck = await DataContext
            .Metadatas.Where(m => m.Id == expiredWhitelisted.Id)
            .FirstOrDefaultAsync();
        deletedCheck.Should().BeNull("expired whitelisted completed metadata should be deleted");

        var recentCheck = await DataContext
            .Metadatas.Where(m => m.Id == recentWhitelisted.Id)
            .FirstOrDefaultAsync();
        recentCheck.Should().NotBeNull("recent metadata should survive");

        var nonWhitelistedCheck = await DataContext
            .Metadatas.Where(m => m.Id == expiredNonWhitelisted.Id)
            .FirstOrDefaultAsync();
        nonWhitelistedCheck.Should().NotBeNull("non-whitelisted metadata should survive");

        var pendingCheck = await DataContext
            .Metadatas.Where(m => m.Id == expiredPending.Id)
            .FirstOrDefaultAsync();
        pendingCheck.Should().NotBeNull("pending metadata should survive");
    }

    #endregion

    #region Configuration Tests

    [Test]
    public void AddTrainType_Generic_AddsTypeName()
    {
        var config = new MetadataCleanupConfiguration();
        config.AddTrainType<ManifestManagerTrain>();

        config.TrainTypeWhitelist.Should().Contain(typeof(ManifestManagerTrain).FullName!);
    }

    [Test]
    public void AddTrainType_String_AddsName()
    {
        var config = new MetadataCleanupConfiguration();
        config.AddTrainType("CustomTrain");

        config.TrainTypeWhitelist.Should().Contain("CustomTrain");
    }

    [Test]
    public void AddTrainType_CanAppendMultipleTypes()
    {
        var config = new MetadataCleanupConfiguration();
        config.AddTrainType<ManifestManagerTrain>();
        config.AddTrainType<MetadataCleanupTrain>();
        config.AddTrainType("ThirdTrain");

        config
            .TrainTypeWhitelist.Should()
            .HaveCount(3)
            .And.Contain(typeof(ManifestManagerTrain).FullName!)
            .And.Contain(typeof(MetadataCleanupTrain).FullName!)
            .And.Contain("ThirdTrain");
    }

    #endregion

    #region Helper Methods

    private async Task<Metadata> CreateAndSaveMetadata(
        string name,
        TrainState state,
        DateTime startTime
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        metadata.TrainState = state;
        metadata.StartTime = startTime;

        if (state is TrainState.Completed or TrainState.Failed)
            metadata.EndTime = startTime.AddSeconds(1);

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    #endregion
}
