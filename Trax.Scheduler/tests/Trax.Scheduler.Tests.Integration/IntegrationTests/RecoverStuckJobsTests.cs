using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the RecoverStuckJobsOnStartup feature in SchedulerStartupService.
/// Verifies that in-progress jobs from before server start are marked as failed on startup.
/// </summary>
[TestFixture]
public class RecoverStuckJobsTests : TestSetup
{
    #region Recovery Behavior Tests

    [Test]
    public async Task StartAsync_WithStuckInProgressJobs_MarksThemAsFailed()
    {
        // Arrange — Create metadata with InProgress state and StartTime in the past
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-5)
        );

        var configuration = CreateRecoveryConfiguration(recoverStuckJobs: true);
        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var updated = await DataContext.Metadatas.FirstAsync(m => m.Id == metadata.Id);

        updated.TrainState.Should().Be(TrainState.Failed);
        updated.EndTime.Should().NotBeNull();
        updated.FailureReason.Should().Contain("Server restarted while job was in progress");
    }

    [Test]
    public async Task StartAsync_WithMultipleStuckJobs_MarksAllAsFailed()
    {
        // Arrange
        var metadataIds = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Stuck_{i}");
            var metadata = await CreateAndSaveMetadata(
                manifest,
                TrainState.InProgress,
                startTime: DateTime.UtcNow.AddMinutes(-10)
            );
            metadataIds.Add(metadata.Id);
        }

        var configuration = CreateRecoveryConfiguration(recoverStuckJobs: true);
        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        foreach (var id in metadataIds)
        {
            var updated = await DataContext.Metadatas.FirstAsync(m => m.Id == id);
            updated.TrainState.Should().Be(TrainState.Failed);
        }
    }

    [Test]
    public async Task StartAsync_WithCompletedJobs_LeavesThemUnchanged()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(
            manifest,
            TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-5)
        );

        var configuration = CreateRecoveryConfiguration(recoverStuckJobs: true);
        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var updated = await DataContext.Metadatas.FirstAsync(m => m.Id == metadata.Id);
        updated.TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task StartAsync_WithFailedJobs_LeavesThemUnchanged()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(
            manifest,
            TrainState.Failed,
            startTime: DateTime.UtcNow.AddMinutes(-5)
        );

        var configuration = CreateRecoveryConfiguration(recoverStuckJobs: true);
        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var updated = await DataContext.Metadatas.FirstAsync(m => m.Id == metadata.Id);
        updated.TrainState.Should().Be(TrainState.Failed);
    }

    [Test]
    public async Task StartAsync_WithRecoveryDisabled_LeavesInProgressJobsUnchanged()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-5)
        );

        var configuration = CreateRecoveryConfiguration(recoverStuckJobs: false);
        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var updated = await DataContext.Metadatas.FirstAsync(m => m.Id == metadata.Id);
        updated
            .TrainState.Should()
            .Be(TrainState.InProgress, "recovery is disabled so jobs should remain unchanged");
    }

    [Test]
    public async Task StartAsync_WithNoStuckJobs_CompletesWithoutError()
    {
        // Arrange — only completed metadata exists
        var manifest = await CreateAndSaveManifest();
        await CreateAndSaveMetadata(
            manifest,
            TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-5)
        );

        var configuration = CreateRecoveryConfiguration(recoverStuckJobs: true);
        var startupService = CreateStartupService(configuration);

        // Act & Assert
        var act = async () => await startupService.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Helper Methods

    private SchedulerStartupService CreateStartupService(SchedulerConfiguration configuration)
    {
        var loggerFactory = Scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var startupLogger = loggerFactory.CreateLogger<SchedulerStartupService>();

        return new SchedulerStartupService(Scope.ServiceProvider, configuration, startupLogger);
    }

    private static SchedulerConfiguration CreateRecoveryConfiguration(bool recoverStuckJobs)
    {
        return new SchedulerConfiguration
        {
            RecoverStuckJobsOnStartup = recoverStuckJobs,
            PruneOrphanedManifests = false,
            HasDatabaseProvider = true,
        };
    }

    private async Task<Manifest> CreateAndSaveManifest(string inputValue = "TestValue")
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
                IntervalSeconds = 60,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = inputValue },
            }
        );

        manifest.ExternalId = $"recover-{Guid.NewGuid():N}";
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

    #endregion
}
