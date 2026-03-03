using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Tests.Integration.Examples.Trains;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for CancelTimedOutJobsStep which runs in the ManifestManagerTrain chain
/// to actively cancel jobs that have exceeded their configured timeout.
/// </summary>
[TestFixture]
public class CancelTimedOutJobsStepTests : TestSetup
{
    private IManifestManagerTrain _train = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
    }

    [TearDown]
    public async Task CancelTimedOutJobsStepTestsTearDown()
    {
        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    [Test]
    public async Task Run_TimedOutJob_SetsCancellationRequested()
    {
        // Arrange — manifest with 60s timeout, InProgress metadata started 120s ago
        var manifest = await CreateManifest(timeoutSeconds: 60);
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddSeconds(-120)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);
        loaded.CancellationRequested.Should().BeTrue();
    }

    [Test]
    public async Task Run_NotTimedOutJob_DoesNotSetCancellationRequested()
    {
        // Arrange — manifest with 300s timeout, InProgress metadata started 30s ago
        var manifest = await CreateManifest(timeoutSeconds: 300);
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddSeconds(-30)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);
        loaded.CancellationRequested.Should().BeFalse();
    }

    [Test]
    public async Task Run_PerManifestTimeout_OverridesDefaultTimeout()
    {
        // Arrange — one manifest with short timeout (timed out), one with long timeout (not timed out)
        var shortTimeout = await CreateManifest(timeoutSeconds: 60);
        var longTimeout = await CreateManifest(timeoutSeconds: 600);

        var timedOutMetadata = await CreateMetadata(
            shortTimeout,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddSeconds(-90)
        );
        var okMetadata = await CreateMetadata(
            longTimeout,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddSeconds(-90)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var timedOut = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == timedOutMetadata.Id);
        timedOut.CancellationRequested.Should().BeTrue("elapsed 90s > timeout 60s");

        var ok = await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == okMetadata.Id);
        ok.CancellationRequested.Should().BeFalse("elapsed 90s < timeout 600s");
    }

    [Test]
    public async Task Run_AlreadyCancellationRequested_NotReprocessed()
    {
        // Arrange — InProgress metadata with CancellationRequested already true
        var manifest = await CreateManifest(timeoutSeconds: 60);
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddSeconds(-120)
        );

        // Set CancellationRequested to true before the step runs
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.CancellationRequested, true),
                CancellationToken.None
            );
        DataContext.Reset();

        // Act — should complete without errors (idempotent)
        var act = async () => await _train.Run(Unit.Default);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_NoActiveExecutions_CompletesWithoutErrors()
    {
        // Arrange — manifest with only Completed metadata
        var manifest = await CreateManifest(timeoutSeconds: 60);
        await CreateMetadata(manifest, TrainState.Completed);

        // Act
        var act = async () => await _train.Run(Unit.Default);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_TimedOutJob_DoesNotCountAsFailed()
    {
        // Arrange — manifest with timeout that will be cancelled, max_retries=1
        // Even though it times out, it should NOT create a dead letter
        // (Cancelled != Failed)
        var manifest = await CreateManifest(timeoutSeconds: 60, maxRetries: 1);
        await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddSeconds(-120)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert — no dead letter created (cancelled, not failed)
        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters.Should().BeEmpty("timed-out cancelled jobs should not create dead letters");
    }

    #region Helper Methods

    private async Task<Manifest> CreateManifest(int? timeoutSeconds = null, int maxRetries = 3)
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
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 3600,
                MaxRetries = maxRetries,
                TimeoutSeconds = timeoutSeconds,
                Properties = new SchedulerTestInput { Value = "TimeoutTest" },
            }
        );
        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Metadata> CreateMetadata(
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
                Input = new SchedulerTestInput { Value = "TimeoutTest" },
                ManifestId = manifest.Id,
            }
        );
        metadata.TrainState = state;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);

        if (startTime.HasValue)
        {
            // Use ExecuteUpdateAsync to set StartTime since it's set by Metadata.Create
            await DataContext
                .Metadatas.Where(m => m.Id == metadata.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.StartTime, startTime.Value),
                    CancellationToken.None
                );
        }

        DataContext.Reset();
        return metadata;
    }

    #endregion
}
