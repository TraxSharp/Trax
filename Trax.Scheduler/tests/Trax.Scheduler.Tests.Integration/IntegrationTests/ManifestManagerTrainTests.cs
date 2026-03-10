using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the ManifestManagerTrain which orchestrates the manifest-based job scheduling system.
/// </summary>
/// <remarks>
/// The ManifestManagerTrain runs through the following steps:
/// 1. LoadManifestsStep - Loads all enabled manifests with their Metadatas, DeadLetters, and WorkQueues
/// 2. ReapFailedJobsStep - Creates DeadLetter records for manifests exceeding retry limits
/// 3. DetermineJobsToQueueStep - Determines which manifests are due for execution
/// 4. CreateWorkQueueEntriesStep - Creates WorkQueue entries for manifests that need to be dispatched
/// </remarks>
[TestFixture]
public class ManifestManagerTrainTests : TestSetup
{
    private IManifestManagerTrain _train = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
    }

    [TearDown]
    public async Task ManifestManagerTrainTestsTearDown()
    {
        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    #region LoadManifestsStep Tests

    [Test]
    public async Task Run_WithEnabledManifest_LoadsManifest()
    {
        // Arrange - Create an enabled manifest
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: true
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - The train should complete without errors
        // Since the manifest is interval-based and never ran, a work queue entry should be created
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries.Should().NotBeEmpty("the manifest should have been queued for execution");
    }

    [Test]
    public async Task Run_WithDisabledManifest_DoesNotLoadManifest()
    {
        // Arrange - Create a disabled manifest
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: false
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created for the disabled manifest
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("disabled manifests should not be processed");
    }

    #endregion

    #region ReapFailedJobsStep Tests

    [Test]
    public async Task Run_WhenManifestExceedsMaxRetries_CreatesDeadLetter()
    {
        // Arrange - Create a manifest with max_retries = 2 and 3 failed executions
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            maxRetries: 2
        );

        // Create 3 failed metadata records
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);

        // Act
        await _train.Run(Unit.Default);

        // Assert - A dead letter should be created
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();

        deadLetters.Should().HaveCount(1);
        deadLetters[0].Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
        deadLetters[0].Reason.Should().Contain("Max retries exceeded");
    }

    [Test]
    public async Task Run_WhenManifestHasNotExceededMaxRetries_DoesNotCreateDeadLetter()
    {
        // Arrange - Create a manifest with max_retries = 3 and only 2 failed executions
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            maxRetries: 3
        );

        // Create 2 failed metadata records (below the threshold)
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);

        // Act
        await _train.Run(Unit.Default);

        // Assert - No dead letter should be created
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();

        deadLetters.Should().BeEmpty("manifest has not exceeded max retries");
    }

    [Test]
    public async Task Run_WhenManifestAlreadyHasAwaitingInterventionDeadLetter_DoesNotCreateDuplicateDeadLetter()
    {
        // Arrange - Create a manifest with an existing dead letter
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            maxRetries: 2
        );

        // Create failed metadata records
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);

        // Create an existing dead letter
        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.AwaitingIntervention);

        // Act
        await _train.Run(Unit.Default);

        // Assert - No duplicate dead letter should be created
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();

        deadLetters.Should().HaveCount(1, "should not create duplicate dead letters");
    }

    #endregion

    #region DetermineJobsToQueueStep Tests

    [Test]
    public async Task Run_WhenIntervalManifestIsDue_EnqueuesJob()
    {
        // Arrange - Create an interval manifest that's never run (immediately due)
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - A work queue entry should be created for the job
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries.Should().HaveCount(1, "interval manifest should be queued");
        workQueueEntries[0].Status.Should().Be(WorkQueueStatus.Queued);
        workQueueEntries[0].TrainName.Should().Be(typeof(SchedulerTestTrain).FullName);
    }

    [Test]
    public async Task Run_WhenIntervalManifestNotYetDue_DoesNotEnqueueJob()
    {
        // Arrange - Create an interval manifest that ran recently
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600 // 1 hour
        );

        // Re-load the manifest to track it and update LastSuccessfulRun
        var trackedManifest = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);
        trackedManifest.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        var lastRunBefore = trackedManifest.LastSuccessfulRun!.Value;
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created since interval hasn't elapsed
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("interval has not elapsed yet");

        // Verify LastSuccessfulRun wasn't changed (within milliseconds due to DB precision)
        var updatedManifest = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);
        updatedManifest
            .LastSuccessfulRun.Should()
            .BeCloseTo(
                lastRunBefore,
                TimeSpan.FromMilliseconds(100),
                "manifest should not have been re-queued"
            );
    }

    [Test]
    public async Task Run_WhenCronManifestIsDue_EnqueuesJob()
    {
        // Arrange - Create a cron manifest with "every minute" expression that never ran
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Cron,
            cronExpression: "* * * * *" // every minute
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - A work queue entry should be created
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries.Should().HaveCount(1, "cron manifest that never ran should be queued");
        workQueueEntries[0].Status.Should().Be(WorkQueueStatus.Queued);
    }

    [Test]
    public async Task Run_WhenManifestHasScheduleTypeNone_DoesNotEnqueueJob()
    {
        // Arrange - Create a manifest with ScheduleType.None (manual only)
        var manifest = await CreateAndSaveManifest(scheduleType: ScheduleType.None);

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty("ScheduleType.None manifests should not be auto-scheduled");
    }

    [Test]
    public async Task Run_WhenManifestHasOnDemandSchedule_DoesNotAutoEnqueue()
    {
        // Arrange - Create an OnDemand manifest
        var manifest = await CreateAndSaveManifest(scheduleType: ScheduleType.OnDemand);

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created (OnDemand is for bulk operations only)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty("OnDemand manifests should only be triggered via BulkEnqueueAsync");
    }

    [Test]
    public async Task Run_WhenManifestHasAwaitingInterventionDeadLetter_DoesNotEnqueueJob()
    {
        // Arrange - Create a manifest with a dead letter
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60
        );

        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.AwaitingIntervention);

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty("manifests with AwaitingIntervention dead letters should be skipped");
    }

    [Test]
    public async Task Run_WhenManifestHasPendingExecution_DoesNotEnqueueJob()
    {
        // Arrange - Create a manifest with a pending execution
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60
        );

        await CreateAndSaveMetadata(manifest, TrainState.Pending);

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created (active metadata prevents queueing)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty("should not queue a manifest that already has pending execution");
    }

    [Test]
    public async Task Run_WhenManifestHasInProgressExecution_DoesNotEnqueueJob()
    {
        // Arrange - Create a manifest with an in-progress execution
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60
        );

        await CreateAndSaveMetadata(manifest, TrainState.InProgress);

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created (active metadata prevents queueing)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty("should not queue a manifest that has in-progress execution");
    }

    #endregion

    #region CreateWorkQueueEntriesStep Tests

    [Test]
    public async Task Run_WhenManifestIsQueued_CreatesWorkQueueWithCorrectManifestId()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var workQueueEntry = await DataContext.WorkQueues.FirstOrDefaultAsync(q =>
            q.ManifestId == manifest.Id
        );

        workQueueEntry.Should().NotBeNull();
        workQueueEntry!.ManifestId.Should().Be(manifest.Id);
    }

    [Test]
    public async Task Run_WhenManifestIsQueued_CreatesWorkQueueWithCorrectTrainName()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - WorkQueue entry should have correct train name and Queued status
        DataContext.Reset();
        var workQueueEntry = await DataContext.WorkQueues.FirstOrDefaultAsync(q =>
            q.ManifestId == manifest.Id
        );

        workQueueEntry.Should().NotBeNull();
        workQueueEntry!.TrainName.Should().Be(typeof(SchedulerTestTrain).FullName);
        workQueueEntry.Status.Should().Be(WorkQueueStatus.Queued);
    }

    #endregion

    #region Full Train Integration Tests

    [Test]
    public async Task Run_WithMultipleManifests_ProcessesEachCorrectly()
    {
        // Arrange - Create multiple manifests with different scenarios
        var enabledIntervalManifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            inputValue: "Enabled_Interval"
        );

        var disabledManifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: false,
            inputValue: "Disabled"
        );

        var manualOnlyManifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.None,
            inputValue: "ManualOnly"
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();

        // Enabled interval manifest should have a work queue entry
        var enabledEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == enabledIntervalManifest.Id)
            .ToListAsync();
        enabledEntries.Should().HaveCount(1);
        enabledEntries[0].Status.Should().Be(WorkQueueStatus.Queued);

        // Disabled manifest should not be processed
        var disabledEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == disabledManifest.Id)
            .ToListAsync();
        disabledEntries.Should().BeEmpty();

        // Manual-only manifest should not be auto-scheduled
        var manualEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manualOnlyManifest.Id)
            .ToListAsync();
        manualEntries.Should().BeEmpty();
    }

    [Test]
    public async Task Run_WithMixOfFailedAndHealthyManifests_DeadLettersCorrectly()
    {
        // Arrange
        var failingManifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            maxRetries: 2,
            inputValue: "Failing"
        );

        // Add 3 failed executions (exceeds max_retries of 2)
        await CreateAndSaveMetadata(failingManifest, TrainState.Failed);
        await CreateAndSaveMetadata(failingManifest, TrainState.Failed);
        await CreateAndSaveMetadata(failingManifest, TrainState.Failed);

        var healthyManifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            inputValue: "Healthy"
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();

        // Failing manifest should have a dead letter and no new executions
        var failingDeadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == failingManifest.Id)
            .ToListAsync();
        failingDeadLetters.Should().HaveCount(1);
        failingDeadLetters[0].Status.Should().Be(DeadLetterStatus.AwaitingIntervention);

        var failingWorkQueues = await DataContext
            .WorkQueues.Where(q => q.ManifestId == failingManifest.Id)
            .ToListAsync();
        failingWorkQueues.Should().BeEmpty("dead-lettered manifests should not be queued");

        // Healthy manifest should have a work queue entry
        var healthyWorkQueues = await DataContext
            .WorkQueues.Where(q => q.ManifestId == healthyManifest.Id)
            .ToListAsync();
        healthyWorkQueues.Should().HaveCount(1);
        healthyWorkQueues[0].Status.Should().Be(WorkQueueStatus.Queued);
    }

    [Test]
    public async Task Run_CompletesSuccessfully_ReturnsUnit()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60
        );

        // Act & Assert - The train should complete successfully without throwing
        var act = async () => await _train.Run(Unit.Default);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_WithNoEnabledManifests_CompletesWithoutErrors()
    {
        // Arrange - Create only disabled manifests
        await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            isEnabled: false
        );

        // Act & Assert - Should complete without throwing
        var act = async () => await _train.Run(Unit.Default);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_WhenManifestAlreadyHasQueuedEntry_DoesNotCreateDuplicateQueueEntry()
    {
        // Arrange - Create an interval manifest
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60
        );

        // Act - Run the train twice (first creates queue entry, second should skip)
        await _train.Run(Unit.Default);

        // Recreate train for second run (fresh scope)
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        await _train.Run(Unit.Default);

        // Assert - Only one work queue entry should exist (double-queue prevention)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .HaveCount(1, "double-queue prevention should stop duplicate Queued entries");
        workQueueEntries[0].Status.Should().Be(WorkQueueStatus.Queued);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Run_WhenCronExpressionIsInvalid_DoesNotCrash()
    {
        // Arrange - Create a manifest with an invalid cron expression
        var manifest = await CreateAndSaveManifestRaw(
            scheduleType: ScheduleType.Cron,
            cronExpression: "invalid-cron",
            intervalSeconds: null
        );

        // Act - Should not throw
        var act = async () => await _train.Run(Unit.Default);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_WhenIntervalSecondsIsZero_DoesNotEnqueue()
    {
        // Arrange - Create a manifest with zero interval
        var manifest = await CreateAndSaveManifestRaw(
            scheduleType: ScheduleType.Interval,
            cronExpression: null,
            intervalSeconds: 0
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - Should not create work queue entry due to invalid interval
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("zero interval should be treated as invalid");
    }

    [Test]
    public async Task Run_WhenIntervalSecondsIsNegative_DoesNotEnqueue()
    {
        // Arrange - Create a manifest with negative interval
        var manifest = await CreateAndSaveManifestRaw(
            scheduleType: ScheduleType.Interval,
            cronExpression: null,
            intervalSeconds: -100
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("negative interval should be treated as invalid");
    }

    [Test]
    public async Task Run_WhenCronExpressionIsNull_DoesNotEnqueue()
    {
        // Arrange - Create a cron manifest without a cron expression
        var manifest = await CreateAndSaveManifestRaw(
            scheduleType: ScheduleType.Cron,
            cronExpression: null,
            intervalSeconds: null
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("cron manifest without expression should not be queued");
    }

    #endregion

    #region DetermineJobsToQueueStep Dependent Tests

    [Test]
    public async Task Run_WhenDependentManifestParentSucceeded_EnqueuesDependent()
    {
        // Arrange - Create a parent manifest that has run successfully
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        // Set parent's LastSuccessfulRun to now (simulating a recent success)
        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Create a dependent manifest that has never run
        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should be queued
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .HaveCount(1, "dependent manifest should be queued after parent succeeds");
        workQueueEntries[0].Status.Should().Be(WorkQueueStatus.Queued);
    }

    [Test]
    public async Task Run_WhenDependentManifestAlreadyRanAfterParent_DoesNotEnqueueDependent()
    {
        // Arrange - Parent ran 10 minutes ago
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-10);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Dependent ran 5 minutes ago (after parent)
        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");
        var trackedDependent = await DataContext.Manifests.FirstAsync(m => m.Id == dependent.Id);
        trackedDependent.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-5);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should NOT be queued (already ran after parent)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("dependent already ran after parent's last success");
    }

    [Test]
    public async Task Run_WhenDependentManifestParentNeverRan_DoesNotEnqueueDependent()
    {
        // Arrange - Parent has never run (LastSuccessfulRun = null)
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should NOT be queued (parent hasn't succeeded yet)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("parent has never run successfully");
    }

    [Test]
    public async Task Run_WhenDependentManifestHasDeadLetter_DoesNotEnqueueDependent()
    {
        // Arrange - Parent ran successfully
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Dependent has a dead letter
        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");
        await CreateAndSaveDeadLetter(dependent, DeadLetterStatus.AwaitingIntervention);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should NOT be queued (has dead letter)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("dependent with dead letter should be skipped");
    }

    [Test]
    public async Task Run_WhenDependentManifestHasActiveExecution_DoesNotEnqueueDependent()
    {
        // Arrange - Parent ran successfully
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Dependent has a pending execution
        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");
        await CreateAndSaveMetadata(dependent, TrainState.Pending);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should NOT be queued (has active execution)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries.Should().BeEmpty("dependent with active execution should be skipped");
    }

    [Test]
    public async Task Run_WhenDependentManifestHasQueuedEntry_DoesNotEnqueueDuplicate()
    {
        // Arrange - Parent ran successfully
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Dependent has never run, but already has a queued entry from a prior cycle
        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");
        await CreateAndSaveWorkQueueEntry(dependent);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Should NOT create a duplicate queue entry
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries.Should().HaveCount(1, "should not create duplicate work queue entry");
    }

    [Test]
    public async Task Run_DependentChain_QueuesOnlyImmediateDependent()
    {
        // Arrange - Chain: A → B → C
        // A ran successfully, B and C have never run
        var manifestA = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "A"
        );

        var trackedA = await DataContext.Manifests.FirstAsync(m => m.Id == manifestA.Id);
        trackedA.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var manifestB = await CreateAndSaveDependentManifest(manifestA, inputValue: "B");
        var manifestC = await CreateAndSaveDependentManifest(manifestB, inputValue: "C");

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();

        // B should be queued (A succeeded, B never ran)
        var bEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifestB.Id)
            .ToListAsync();
        bEntries.Should().HaveCount(1, "B should be queued because A succeeded");

        // C should NOT be queued (B hasn't succeeded yet)
        var cEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifestC.Id)
            .ToListAsync();
        cEntries.Should().BeEmpty("C should not be queued because B hasn't succeeded yet");
    }

    [Test]
    public async Task Run_WhenManifestHasDormantDependentScheduleType_DoesNotAutoEnqueue()
    {
        // Arrange - Create a DormantDependent manifest (should never be auto-enqueued)
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            inputValue: "Parent"
        );

        var dormant = await CreateAndSaveDormantDependentManifest(parent, inputValue: "Dormant");

        // Act
        await _train.Run(Unit.Default);

        // Assert - DormantDependent should NOT have any work queue entries
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dormant.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty("DormantDependent manifests should never be auto-enqueued by ManifestManager");
    }

    [Test]
    public async Task Run_WhenDependentManifestIsDormant_DoesNotEnqueueOnParentSuccess()
    {
        // Arrange - Parent ran successfully, dormant dependent has never run
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Create a dormant dependent (unlike normal Dependent, should NOT auto-fire)
        var dormant = await CreateAndSaveDormantDependentManifest(
            parent,
            inputValue: "DormantChild"
        );

        // Also create a normal dependent for comparison (should be queued)
        var normalDependent = await CreateAndSaveDependentManifest(
            parent,
            inputValue: "NormalChild"
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();

        // Normal dependent SHOULD be queued (parent succeeded, dependent never ran)
        var normalEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == normalDependent.Id)
            .ToListAsync();
        normalEntries
            .Should()
            .HaveCount(1, "normal dependent should be queued after parent success");

        // Dormant dependent should NOT be queued (requires explicit activation)
        var dormantEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dormant.Id)
            .ToListAsync();
        dormantEntries
            .Should()
            .BeEmpty(
                "dormant dependents must be explicitly activated via IDormantDependentContext"
            );
    }

    #endregion

    #region Once Schedule Tests

    [Test]
    public async Task Run_WhenOnceManifestIsDue_EnqueuesJob()
    {
        // Arrange - Create a Once manifest with ScheduledAt in the past that has never run
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Once,
            scheduledAt: DateTime.UtcNow.AddMinutes(-5),
            inputValue: "OnceJob_Due"
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - A work queue entry should be created
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .HaveCount(1, "Once manifest with past ScheduledAt should be queued");
        workQueueEntries[0].Status.Should().Be(WorkQueueStatus.Queued);
        workQueueEntries[0].TrainName.Should().Be(typeof(SchedulerTestTrain).FullName);
    }

    [Test]
    public async Task Run_WhenOnceManifestNotYetDue_DoesNotEnqueueJob()
    {
        // Arrange - Create a Once manifest with ScheduledAt in the future
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Once,
            scheduledAt: DateTime.UtcNow.AddHours(1),
            inputValue: "OnceJob_Future"
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty("Once manifest with future ScheduledAt should not be queued yet");
    }

    [Test]
    public async Task Run_WhenOnceManifestAlreadyRan_DoesNotEnqueueJob()
    {
        // Arrange - Create a Once manifest with ScheduledAt in the past
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Once,
            scheduledAt: DateTime.UtcNow.AddMinutes(-30),
            inputValue: "OnceJob_AlreadyRan"
        );

        // Set LastSuccessfulRun to simulate that it already ran
        var loaded = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);
        loaded.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-25);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert - No work queue entry should be created
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty(
                "Once manifest that has already run (LastSuccessfulRun set) should not be re-queued"
            );
    }

    #endregion

    #region Helper Methods

    private async Task<Manifest> CreateAndSaveManifest(
        ScheduleType scheduleType = ScheduleType.None,
        int? intervalSeconds = null,
        string? cronExpression = null,
        int maxRetries = 3,
        bool isEnabled = true,
        string inputValue = "TestValue",
        DateTime? scheduledAt = null
    )
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
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
                CronExpression = cronExpression,
                MaxRetries = maxRetries,
                Properties = new SchedulerTestInput { Value = inputValue },
                ScheduledAt = scheduledAt,
            }
        );

        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    /// <summary>
    /// Creates a manifest with raw values for edge case testing.
    /// </summary>
    private async Task<Manifest> CreateAndSaveManifestRaw(
        ScheduleType scheduleType,
        string? cronExpression,
        int? intervalSeconds,
        bool isEnabled = true
    )
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
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
                CronExpression = cronExpression,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "EdgeCase" },
            }
        );

        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Metadata> CreateAndSaveMetadata(Manifest manifest, TrainState state)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = manifest.GetProperties<SchedulerTestInput>(),
                ManifestId = manifest.Id,
            }
        );

        metadata.TrainState = state;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    private async Task<DeadLetter> CreateAndSaveDeadLetter(
        Manifest manifest,
        DeadLetterStatus status
    )
    {
        // Reload the manifest from the current DataContext to avoid EF tracking issues
        var reloadedManifest = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = reloadedManifest,
                Reason = "Test dead letter",
                RetryCount = 3,
            }
        );

        deadLetter.Status = status;

        await DataContext.Track(deadLetter);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return deadLetter;
    }

    private async Task<Manifest> CreateAndSaveDependentManifest(
        Manifest parent,
        string inputValue = "Dependent"
    )
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Dependent,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = inputValue },
                DependsOnManifestId = parent.Id,
            }
        );

        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<WorkQueue> CreateAndSaveWorkQueueEntry(Manifest manifest)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
            }
        );

        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return entry;
    }

    private async Task<Manifest> CreateAndSaveDormantDependentManifest(
        Manifest parent,
        string inputValue = "Dormant"
    )
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.DormantDependent,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = inputValue },
                DependsOnManifestId = parent.Id,
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
