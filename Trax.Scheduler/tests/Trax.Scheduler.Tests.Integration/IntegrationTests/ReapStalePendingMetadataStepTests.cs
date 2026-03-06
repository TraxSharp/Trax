using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Examples.Trains;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for ReapStalePendingMetadataStep which runs in the ManifestManagerTrain chain
/// to automatically fail Pending metadata that has not been picked up within the configured timeout.
/// </summary>
[TestFixture]
public class ReapStalePendingMetadataStepTests : TestSetup
{
    private IManifestManagerTrain _train = null!;
    private SchedulerConfiguration _config = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
    }

    [TearDown]
    public async Task ReapStalePendingMetadataStepTestsTearDown()
    {
        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    [Test]
    public async Task Run_StalePendingMetadata_MarkedAsFailed()
    {
        // Arrange — Pending metadata started well beyond the stale pending timeout
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-30)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded.TrainState.Should().Be(TrainState.Failed);
        loaded.EndTime.Should().NotBeNull();
        loaded.FailureReason.Should().Contain("stale pending timeout");
    }

    [Test]
    public async Task Run_RecentPendingMetadata_NotAffected()
    {
        // Arrange — Pending metadata started recently (within timeout)
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-2)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded
            .TrainState.Should()
            .Be(TrainState.Pending, "recent pending metadata should not be reaped");
    }

    [Test]
    public async Task Run_InProgressMetadata_NotAffected()
    {
        // Arrange — InProgress metadata (not Pending) started long ago
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-30)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded
            .TrainState.Should()
            .Be(
                TrainState.InProgress,
                "InProgress metadata is handled by CancelTimedOutJobsStep, not the stale pending reaper"
            );
    }

    [Test]
    public async Task Run_CompletedMetadata_NotAffected()
    {
        // Arrange — Completed metadata (already terminal)
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-30)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded.TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task Run_FailedMetadata_NotAffected()
    {
        // Arrange — Failed metadata (already terminal)
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Failed,
            startTime: DateTime.UtcNow.AddMinutes(-30)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded.TrainState.Should().Be(TrainState.Failed);
    }

    [Test]
    public async Task Run_MultipleStalePendingMetadata_AllMarkedAsFailed()
    {
        // Arrange — Three stale Pending metadata records
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var manifest1 = await CreateManifest();
        var metadata1 = await CreateMetadata(
            manifest1,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-20)
        );

        var manifest2 = await CreateManifest();
        var metadata2 = await CreateMetadata(
            manifest2,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-15)
        );

        var manifest3 = await CreateManifest();
        var metadata3 = await CreateMetadata(
            manifest3,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-60)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var ids = new[] { metadata1.Id, metadata2.Id, metadata3.Id };
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .ToListAsync();

        loaded.Should().HaveCount(3);
        loaded
            .Should()
            .AllSatisfy(m =>
            {
                m.TrainState.Should().Be(TrainState.Failed);
                m.EndTime.Should().NotBeNull();
                m.FailureReason.Should().Contain("stale pending timeout");
            });
    }

    [Test]
    public async Task Run_MixOfStaleAndRecent_OnlyStaleFailed()
    {
        // Arrange — One stale, one recent
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var staleManifest = await CreateManifest();
        var staleMetadata = await CreateMetadata(
            staleManifest,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-20)
        );

        var recentManifest = await CreateManifest();
        var recentMetadata = await CreateMetadata(
            recentManifest,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-2)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var staleLoaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == staleMetadata.Id);
        staleLoaded.TrainState.Should().Be(TrainState.Failed, "stale metadata should be reaped");

        var recentLoaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == recentMetadata.Id);
        recentLoaded
            .TrainState.Should()
            .Be(TrainState.Pending, "recent metadata should remain pending");
    }

    [Test]
    public async Task Run_NoManifests_CompletesWithoutErrors()
    {
        // Arrange — No manifests, no metadata
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        // Act & Assert
        var act = async () => await _train.Run(Unit.Default);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_StalePendingMetadata_FeedsIntoDeadLetterPipeline()
    {
        // Arrange — Manifest with maxRetries=0 and one stale Pending metadata.
        // After the reaper marks it Failed, ReapFailedJobsStep should dead-letter it
        // in the same ManifestManager cycle.
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var manifest = await CreateManifest(maxRetries: 0);
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-20)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert — metadata should be Failed AND a dead letter should exist
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);
        loaded.TrainState.Should().Be(TrainState.Failed);

        var deadLetters = await DataContext
            .DeadLetters.Where(dl => dl.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Should()
            .HaveCount(1, "ReapFailedJobsStep should dead-letter the manifest in the same cycle");
    }

    [Test]
    public async Task Run_TimeoutEdge_ExactlyAtBoundaryNotReaped()
    {
        // Arrange — Pending metadata started exactly at the timeout boundary (should NOT be reaped)
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Pending,
            // Use 9 minutes ago — within the 10 min timeout
            startTime: DateTime.UtcNow.AddMinutes(-9)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded
            .TrainState.Should()
            .Be(TrainState.Pending, "metadata within timeout should not be reaped");
    }

    [Test]
    public async Task Run_ConfigurableTimeout_RespectsCustomValue()
    {
        // Arrange — Short timeout, Pending metadata that's old enough
        _config.StalePendingTimeout = TimeSpan.FromSeconds(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-1)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded
            .TrainState.Should()
            .Be(TrainState.Failed, "60 seconds > 30 second timeout, should be reaped");
    }

    [Test]
    public async Task Run_MetadataWithoutManifest_StillReaped()
    {
        // Arrange — Pending metadata without a manifest (manual/ad-hoc execution)
        _config.StalePendingTimeout = TimeSpan.FromMinutes(10);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "ManualTest" },
                ManifestId = null,
            }
        );
        metadata.TrainState = TrainState.Pending;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);

        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.StartTime, DateTime.UtcNow.AddMinutes(-20)),
                CancellationToken.None
            );
        DataContext.Reset();

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded
            .TrainState.Should()
            .Be(TrainState.Failed, "orphaned manual metadata should be reaped too");
    }

    #region Helper Methods

    private async Task<Manifest> CreateManifest(int maxRetries = 3)
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
                Properties = new SchedulerTestInput { Value = "StalePendingTest" },
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
                Input = new SchedulerTestInput { Value = "StalePendingTest" },
                ManifestId = manifest.Id,
            }
        );
        metadata.TrainState = state;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);

        if (startTime.HasValue)
        {
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
