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
/// The ManifestManagerTrain runs through the following junctions:
/// 1. LoadManifestsJunction - Loads all enabled manifests with their Metadatas, DeadLetters, and WorkQueues
/// 2. ReapFailedJobsJunction - Creates DeadLetter records for manifests exceeding retry limits
/// 3. DetermineJobsToQueueJunction - Determines which manifests are due for execution
/// 4. CreateWorkQueueEntriesJunction - Creates WorkQueue entries for manifests that need to be dispatched
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

    #region LoadManifestsJunction Tests

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

    #region ReapFailedJobsJunction Tests

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

    #region DetermineJobsToQueueJunction Tests

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

    #region CreateWorkQueueEntriesJunction Tests

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

    #region DetermineJobsToQueueJunction Dependent Tests

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

        // Create a successful metadata record to back up the timestamp
        await CreateAndSaveMetadata(parent, TrainState.Completed);

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

        // Create a successful metadata record to back up the timestamp
        await CreateAndSaveMetadata(parent, TrainState.Completed);

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

        // Create a successful metadata record to back up the timestamp
        await CreateAndSaveMetadata(parent, TrainState.Completed);

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

        // Create a successful metadata record to back up the timestamp
        await CreateAndSaveMetadata(parent, TrainState.Completed);

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

        // Create a successful metadata record to back up the timestamp
        await CreateAndSaveMetadata(parent, TrainState.Completed);

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

        // Create a successful metadata record to back up the timestamp
        await CreateAndSaveMetadata(manifestA, TrainState.Completed);

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

        // Create a successful metadata record to back up the timestamp
        await CreateAndSaveMetadata(parent, TrainState.Completed);

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

    #region Stale LastSuccessfulRun Guard Tests

    [Test]
    public async Task Run_WhenDependentManifestParentHasStaleLastSuccessfulRun_DoesNotEnqueueDependent()
    {
        // Arrange - Parent has LastSuccessfulRun set but NO metadata records at all.
        // This simulates metadata being truncated/pruned while the manifest retains a stale timestamp.
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Deliberately NOT creating any metadata — timestamp is orphaned

        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should NOT be queued because parent has no successful metadata
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty(
                "parent has LastSuccessfulRun but no successful metadata to back it up — timestamp is stale"
            );
    }

    [Test]
    public async Task Run_WhenDependentManifestParentHasValidLastSuccessfulRun_EnqueuesDependent()
    {
        // Arrange - Parent has both LastSuccessfulRun AND a Completed metadata record
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await CreateAndSaveMetadata(parent, TrainState.Completed);

        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should be queued (timestamp backed by real metadata)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .HaveCount(1, "parent has LastSuccessfulRun backed by a Completed metadata record");
    }

    [Test]
    public async Task Run_WhenDependentManifestParentHasOnlyFailedMetadata_DoesNotEnqueueDependent()
    {
        // Arrange - Parent has LastSuccessfulRun but only Failed metadata (no Completed).
        // This can happen when a prior success was pruned and new failures accumulated.
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Only failed metadata — no Completed record
        await CreateAndSaveMetadata(parent, TrainState.Failed);
        await CreateAndSaveMetadata(parent, TrainState.Failed);

        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should NOT be queued
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty(
                "parent has only Failed metadata — no Completed record to verify the timestamp"
            );
    }

    [Test]
    public async Task Run_WhenDependentManifestParentHasOnlyInProgressMetadata_DoesNotEnqueueDependent()
    {
        // Arrange - Parent has LastSuccessfulRun but only InProgress/Pending metadata.
        // Parent is currently running but hasn't completed yet in this cycle.
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Only in-progress metadata — no Completed record
        await CreateAndSaveMetadata(parent, TrainState.Pending);
        await CreateAndSaveMetadata(parent, TrainState.InProgress);

        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should NOT be queued
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .BeEmpty(
                "parent has only Pending/InProgress metadata — no Completed record to verify the timestamp"
            );
    }

    [Test]
    public async Task Run_DependentChain_ParentHasStaleTimestamp_NeitherDependentQueued()
    {
        // Arrange - Chain: A → B → C
        // A has LastSuccessfulRun set but no Completed metadata (stale timestamp)
        var manifestA = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "A"
        );

        var trackedA = await DataContext.Manifests.FirstAsync(m => m.Id == manifestA.Id);
        trackedA.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Deliberately NOT creating metadata for A — timestamp is stale

        var manifestB = await CreateAndSaveDependentManifest(manifestA, inputValue: "B");
        var manifestC = await CreateAndSaveDependentManifest(manifestB, inputValue: "C");

        // Act
        await _train.Run(Unit.Default);

        // Assert - Neither B nor C should be queued
        DataContext.Reset();

        var bEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifestB.Id)
            .ToListAsync();
        bEntries
            .Should()
            .BeEmpty("B should not be queued because A has stale LastSuccessfulRun (no metadata)");

        var cEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifestC.Id)
            .ToListAsync();
        cEntries.Should().BeEmpty("C should not be queued because B hasn't succeeded");
    }

    [Test]
    public async Task Run_WhenDependentManifestParentHasCompletedAndFailedMetadata_EnqueuesDependent()
    {
        // Arrange - Parent has both Completed AND Failed metadata.
        // As long as at least one Completed exists, the guard should pass.
        var parent = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 3600,
            inputValue: "Parent"
        );

        var trackedParent = await DataContext.Manifests.FirstAsync(m => m.Id == parent.Id);
        trackedParent.LastSuccessfulRun = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Mix of Completed and Failed metadata
        await CreateAndSaveMetadata(parent, TrainState.Completed);
        await CreateAndSaveMetadata(parent, TrainState.Failed);
        await CreateAndSaveMetadata(parent, TrainState.Failed);

        var dependent = await CreateAndSaveDependentManifest(parent, inputValue: "Dependent");

        // Act
        await _train.Run(Unit.Default);

        // Assert - Dependent should be queued (at least one Completed metadata exists)
        DataContext.Reset();
        var workQueueEntries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dependent.Id)
            .ToListAsync();

        workQueueEntries
            .Should()
            .HaveCount(
                1,
                "parent has at least one Completed metadata record despite also having failures"
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

    #region Misfire Policy Tests

    [Test]
    public async Task Run_IntervalOverdueBeyondThreshold_FireOnceNow_QueuesJob()
    {
        // Arrange — interval=300s, lastRun=12min ago, default policy=FireOnceNow
        // scheduledTime = lastRun + 300s = 7min ago, overdue 420s > default threshold 60s
        // FireOnceNow fires regardless
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 300,
            inputValue: "MisfireFireOnceNow"
        );

        var loaded = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);
        loaded.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-12);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries
            .Should()
            .NotBeEmpty(
                "FireOnceNow policy should queue the job even when overdue beyond threshold"
            );
    }

    [Test]
    public async Task Run_IntervalOverdueBeyondThreshold_DoNothing_SkipsJob()
    {
        // Arrange — interval=300s, lastRun=12min (720s) ago, policy=DoNothing, threshold=60s
        // scheduledTime = lastRun + 300s = 7min ago (overdue 420s > 60s)
        // DoNothing boundary: totalElapsed=720s, missedPeriods=floor(720/300)=2
        // mostRecentBoundary = lastRun + 600s = 2min ago, sinceBoundary=120s > 60s → skip
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 300,
            misfirePolicy: MisfirePolicy.DoNothing,
            misfireThresholdSeconds: 60,
            inputValue: "MisfireDoNothing_Skip"
        );

        var loaded = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);
        loaded.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-12);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries
            .Should()
            .BeEmpty(
                "DoNothing policy should skip the job when overdue beyond threshold of most recent boundary"
            );
    }

    [Test]
    public async Task Run_IntervalOverdue_DoNothing_WithinBoundaryThreshold_QueuesJob()
    {
        // Arrange — interval=300s, lastRun=10min 30s (630s) ago, policy=DoNothing, threshold=60s
        // scheduledTime = lastRun + 300s = 5min30s ago (overdue 330s > 60s)
        // DoNothing boundary: totalElapsed=630s, missedPeriods=floor(630/300)=2
        // mostRecentBoundary = lastRun + 600s = 30s ago, sinceBoundary=30s ≤ 60s → fire
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 300,
            misfirePolicy: MisfirePolicy.DoNothing,
            misfireThresholdSeconds: 60,
            inputValue: "MisfireDoNothing_Fire"
        );

        var loaded = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);
        loaded.LastSuccessfulRun = DateTime.UtcNow.AddSeconds(-630);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries
            .Should()
            .NotBeEmpty(
                "DoNothing policy should queue the job when within threshold of most recent boundary"
            );
    }

    #endregion

    #region Schedule Variance Tests

    [Test]
    public async Task Run_WithVarianceAndNextScheduledRunInFuture_DoesNotEnqueueJob()
    {
        // Arrange - Create an interval manifest with NextScheduledRun in the future.
        // Use raw SQL to set columns directly since CreateAndSaveManifest detaches entities.
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 300,
            inputValue: "VarianceFuture"
        );

        await DataContext
            .Manifests.Where(m => m.Id == manifest.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.LastSuccessfulRun, DateTime.UtcNow.AddMinutes(-3))
                    .SetProperty(m => m.VarianceSeconds, 120)
                    .SetProperty(m => m.NextScheduledRun, DateTime.UtcNow.AddMinutes(5))
            );
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert - Should NOT be queued because NextScheduledRun is in the future
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries
            .Should()
            .BeEmpty("manifest should not be queued when NextScheduledRun is in the future");
    }

    [Test]
    public async Task Run_WithVarianceAndNextScheduledRunInPast_EnqueuesJob()
    {
        // Arrange - Create an interval manifest with NextScheduledRun in the past.
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 300,
            inputValue: "VariancePast"
        );

        await DataContext
            .Manifests.Where(m => m.Id == manifest.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.LastSuccessfulRun, DateTime.UtcNow.AddMinutes(-10))
                    .SetProperty(m => m.VarianceSeconds, 120)
                    .SetProperty(m => m.NextScheduledRun, DateTime.UtcNow.AddMinutes(-2))
            );
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert - Should be queued because NextScheduledRun has passed
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries
            .Should()
            .NotBeEmpty("manifest should be queued when NextScheduledRun is in the past");
    }

    [Test]
    public async Task Run_WithVarianceAndNoNextScheduledRun_FallsBackToInterval()
    {
        // Arrange - Create an interval manifest that has never run (LastSuccessfulRun=null).
        // Variance is set but NextScheduledRun is null — should fall back to immediate fire.
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 60,
            inputValue: "VarianceFallback"
        );

        await DataContext
            .Manifests.Where(m => m.Id == manifest.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.VarianceSeconds, 30));
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert - Should be queued because never ran (fallback: lastRun is null → fire immediately)
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == manifest.Id)
            .ToListAsync();

        entries
            .Should()
            .NotBeEmpty(
                "manifest with no LastSuccessfulRun should fire immediately regardless of variance"
            );
    }

    #endregion

    #region Dead Letter Retry Starvation Tests

    [Test]
    public async Task Run_WhenDeadLetterRetried_DoesNotImmediatelyReDeadLetter()
    {
        // Arrange - manifest with 3 old failures (at maxRetries), dead letter resolved via retry
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 3
        );

        var oldTime = DateTime.UtcNow.AddHours(-2);
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: oldTime.AddMinutes(-3));
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: oldTime.AddMinutes(-2));
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: oldTime.AddMinutes(-1));

        // Dead letter was created and then retried (resolved)
        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.Retried, resolvedAt: oldTime);

        // Act
        await _train.Run(Unit.Default);

        // Assert - no new dead letter should be created (old failures are "forgiven")
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(0, "resolved dead letter should reset the failure count");
    }

    [Test]
    public async Task Run_WhenDeadLetterRetriedAndRetryFails_CountsOnlyPostResolutionFailures()
    {
        // Arrange - 3 old failures, resolved dead letter, 1 new failure (below maxRetries=2)
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 2
        );

        var resolutionTime = DateTime.UtcNow.AddHours(-1);
        // Old failures before resolution
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(-30)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(-20)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(-10)
        );

        await CreateAndSaveDeadLetter(
            manifest,
            DeadLetterStatus.Retried,
            resolvedAt: resolutionTime
        );

        // 1 new failure after resolution (below maxRetries=2)
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(5)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - only 1 post-resolution failure, below maxRetries=2
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(0, "only 1 post-resolution failure which is below maxRetries=2");
    }

    [Test]
    public async Task Run_WhenDeadLetterRetriedAndNewFailuresExceedMaxRetries_CreatesNewDeadLetter()
    {
        // Arrange - old failures, resolved dead letter, 2 new failures (= maxRetries=2)
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 2
        );

        var resolutionTime = DateTime.UtcNow.AddHours(-1);
        // Old failures
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(-30)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(-20)
        );

        await CreateAndSaveDeadLetter(
            manifest,
            DeadLetterStatus.Retried,
            resolvedAt: resolutionTime
        );

        // 2 new failures after resolution (meets maxRetries=2)
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(5)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(10)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - 2 post-resolution failures >= maxRetries=2, new dead letter created
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(1, "2 post-resolution failures should trigger a new dead letter");
    }

    [Test]
    public async Task Run_WhenMultipleDeadLettersResolved_CountsFailuresAfterMostRecent()
    {
        // Arrange - two resolved dead letters at different times, 1 failure after the second
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 2
        );

        var dl1Time = DateTime.UtcNow.AddHours(-3);
        var dl2Time = DateTime.UtcNow.AddHours(-1);

        // Old failures before DL1
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: dl1Time.AddMinutes(-20)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: dl1Time.AddMinutes(-10)
        );

        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.Retried, resolvedAt: dl1Time);

        // Mid failures between DL1 and DL2
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: dl1Time.AddMinutes(10));
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: dl1Time.AddMinutes(20));

        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.Retried, resolvedAt: dl2Time);

        // 1 failure after DL2 (below maxRetries=2)
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: dl2Time.AddMinutes(5));

        // Act
        await _train.Run(Unit.Default);

        // Assert - only 1 failure after most recent resolution, below threshold
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(0, "only 1 failure after the most recent dead letter resolution");
    }

    [Test]
    public async Task Run_WhenDeadLetterAcknowledged_AlsoResetsFailureCount()
    {
        // Arrange - acknowledged dead letter should also reset the failure count
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 3
        );

        var oldTime = DateTime.UtcNow.AddHours(-2);
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: oldTime.AddMinutes(-3));
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: oldTime.AddMinutes(-2));
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: oldTime.AddMinutes(-1));

        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.Acknowledged, resolvedAt: oldTime);

        // Act
        await _train.Run(Unit.Default);

        // Assert - acknowledged dead letter resets count just like retried
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(0, "acknowledged dead letter should also reset the failure count");
    }

    [Test]
    public async Task Run_WhenNoResolvedDeadLetters_CountsAllFailures()
    {
        // Arrange - no dead letters at all, 3 failures exceed maxRetries=2 (regression test)
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 2
        );

        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);

        // Act
        await _train.Run(Unit.Default);

        // Assert - backward compatible: all failures counted, dead letter created
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(1, "without any resolved dead letters, all failures should count");
    }

    [Test]
    public async Task Run_WhenAwaitingInterventionDeadLetterExists_SkipsManifestEntirely()
    {
        // Arrange - unresolved dead letter blocks the manifest regardless of failure count
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 2
        );

        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);

        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.AwaitingIntervention);

        // Act
        await _train.Run(Unit.Default);

        // Assert - only the original dead letter exists (no duplicate)
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters.Should().HaveCount(1, "should not create duplicate dead letters");
        deadLetters[0].Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
    }

    [Test]
    public async Task Run_WhenDeadLetterRetriedAndRetrySucceedsThenFailsAgain_CountsCorrectly()
    {
        // Arrange - resolved DL, 1 success + 1 failure after resolution
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 2
        );

        var resolutionTime = DateTime.UtcNow.AddHours(-1);
        // Old failures
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(-10)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(-5)
        );

        await CreateAndSaveDeadLetter(
            manifest,
            DeadLetterStatus.Retried,
            resolvedAt: resolutionTime
        );

        // Retry succeeded, then failed again (1 success + 1 failure post-resolution)
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Completed,
            startTime: resolutionTime.AddMinutes(5)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: resolutionTime.AddMinutes(10)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - only 1 post-resolution failure, below maxRetries=2
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(0, "only 1 post-resolution failure, below maxRetries=2");
    }

    [Test]
    public async Task Run_WhenDeadLetterResolvedAtNull_TreatedAsUnresolved()
    {
        // Arrange - malformed dead letter with Retried status but no ResolvedAt
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 2
        );

        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);
        await CreateAndSaveMetadata(manifest, TrainState.Failed);

        // Dead letter with Retried status but null ResolvedAt (malformed data)
        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.Retried, resolvedAt: null);

        // Act
        await _train.Run(Unit.Default);

        // Assert - null ResolvedAt means the NOT EXISTS subquery doesn't match,
        // so all failures count. A new dead letter should be created.
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(1, "null ResolvedAt should not reset failure count — fail safe");
    }

    [Test]
    public async Task Run_MultipleRetryCycles_EachResolutionResetsCount()
    {
        // Arrange - manifest goes through multiple dead letter → retry → fail cycles
        var manifest = await CreateAndSaveManifest(
            scheduleType: ScheduleType.Interval,
            intervalSeconds: 1,
            maxRetries: 2
        );

        // Cycle 1: 2 failures → dead letter → resolved
        var dl1Time = DateTime.UtcNow.AddHours(-4);
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: dl1Time.AddMinutes(-20)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: dl1Time.AddMinutes(-10)
        );
        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.Retried, resolvedAt: dl1Time);

        // Cycle 2: 2 more failures → dead letter → resolved
        var dl2Time = DateTime.UtcNow.AddHours(-2);
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: dl2Time.AddMinutes(-20)
        );
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: dl2Time.AddMinutes(-10)
        );
        await CreateAndSaveDeadLetter(manifest, DeadLetterStatus.Retried, resolvedAt: dl2Time);

        // Cycle 3: only 1 failure so far (below threshold)
        await CreateAndSaveMetadata(manifest, TrainState.Failed, startTime: dl2Time.AddMinutes(5));

        // Act
        await _train.Run(Unit.Default);

        // Assert - only 1 failure after most recent resolution, below maxRetries=2
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Count(dl => dl.Status == DeadLetterStatus.AwaitingIntervention)
            .Should()
            .Be(0, "each resolution resets the count; only 1 failure after DL2");
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
        DateTime? scheduledAt = null,
        MisfirePolicy misfirePolicy = MisfirePolicy.FireOnceNow,
        int? misfireThresholdSeconds = null
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
                MisfirePolicy = misfirePolicy,
                MisfireThresholdSeconds = misfireThresholdSeconds,
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

    private async Task<Metadata> CreateAndSaveMetadata(
        Manifest manifest,
        TrainState state,
        DateTime? startTime = null
    )
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

        if (startTime.HasValue)
            metadata.StartTime = startTime.Value;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    private async Task<DeadLetter> CreateAndSaveDeadLetter(
        Manifest manifest,
        DeadLetterStatus status,
        DateTime? resolvedAt = null
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

        if (resolvedAt.HasValue)
            deadLetter.ResolvedAt = resolvedAt.Value;

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
