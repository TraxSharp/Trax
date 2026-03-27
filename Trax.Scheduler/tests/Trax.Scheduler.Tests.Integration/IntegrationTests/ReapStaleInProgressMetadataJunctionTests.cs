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
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for ReapStaleInProgressMetadataJunction which runs in the ManifestManagerTrain
/// chain to automatically fail InProgress metadata that has not completed within the configured timeout.
/// </summary>
[TestFixture]
public class ReapStaleInProgressMetadataJunctionTests : TestSetup
{
    private IManifestManagerTrain _train = null!;
    private SchedulerConfiguration _config = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();

        // Prevent other pipeline junctions from interfering with test data
        _config.StalePendingTimeout = TimeSpan.FromHours(24);
        _config.DefaultJobTimeout = TimeSpan.FromHours(24);
    }

    [TearDown]
    public async Task ReapStaleInProgressMetadataJunctionTestsTearDown()
    {
        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    #region StaleInProgress

    [Test]
    public async Task Run_StaleInProgressMetadata_MarkedAsFailed()
    {
        // Arrange — InProgress metadata started well beyond the stale in-progress timeout
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-60)
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
        loaded.FailureReason.Should().Contain("stale in-progress timeout");
        loaded.FailureException.Should().Be("StaleInProgressTimeout");
        loaded.FailureJunction.Should().Be("ReapStaleInProgressMetadataJunction");
    }

    [Test]
    public async Task Run_RecentInProgressMetadata_NotAffected()
    {
        // Arrange — InProgress metadata started recently (within timeout)
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-10)
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
            .Be(TrainState.InProgress, "recent in-progress metadata should not be reaped");
    }

    [Test]
    public async Task Run_PendingMetadata_NotAffected()
    {
        // Arrange — Pending metadata (not InProgress) started long ago
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Pending,
            startTime: DateTime.UtcNow.AddMinutes(-60)
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
                TrainState.Pending,
                "Pending metadata is handled by ReapStalePendingMetadataJunction, not the stale in-progress reaper"
            );
    }

    [Test]
    public async Task Run_CompletedMetadata_NotAffected()
    {
        // Arrange — Completed metadata (already terminal)
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-60)
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
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Failed,
            startTime: DateTime.UtcNow.AddMinutes(-60)
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
    public async Task Run_CancelledMetadata_NotAffected()
    {
        // Arrange — Cancelled metadata (already terminal)
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.Cancelled,
            startTime: DateTime.UtcNow.AddMinutes(-60)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        loaded.TrainState.Should().Be(TrainState.Cancelled);
    }

    #endregion

    #region BatchAndMixed

    [Test]
    public async Task Run_MultipleStaleInProgressMetadata_AllMarkedAsFailed()
    {
        // Arrange — Three stale InProgress metadata records
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest1 = await CreateManifest();
        var metadata1 = await CreateMetadata(
            manifest1,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-45)
        );

        var manifest2 = await CreateManifest();
        var metadata2 = await CreateMetadata(
            manifest2,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-60)
        );

        var manifest3 = await CreateManifest();
        var metadata3 = await CreateMetadata(
            manifest3,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddHours(-2)
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
                m.FailureReason.Should().Contain("stale in-progress timeout");
            });
    }

    [Test]
    public async Task Run_MixOfStaleAndRecent_OnlyStaleFailed()
    {
        // Arrange — One stale, one recent
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var staleManifest = await CreateManifest();
        var staleMetadata = await CreateMetadata(
            staleManifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-60)
        );

        var recentManifest = await CreateManifest();
        var recentMetadata = await CreateMetadata(
            recentManifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-5)
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var staleLoaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == staleMetadata.Id);
        staleLoaded
            .TrainState.Should()
            .Be(TrainState.Failed, "stale in-progress metadata should be reaped");

        var recentLoaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == recentMetadata.Id);
        recentLoaded
            .TrainState.Should()
            .Be(TrainState.InProgress, "recent in-progress metadata should remain");
    }

    #endregion

    #region EdgeCases

    [Test]
    public async Task Run_NoManifests_CompletesWithoutErrors()
    {
        // Arrange — No manifests, no metadata
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        // Act & Assert
        var act = async () => await _train.Run(Unit.Default);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_ConfigurableTimeout_RespectsCustomValue()
    {
        // Arrange — Short timeout, InProgress metadata that's old enough
        _config.StaleInProgressTimeout = TimeSpan.FromSeconds(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
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
    public async Task Run_TimeoutEdge_WithinBoundaryNotReaped()
    {
        // Arrange — InProgress metadata started within the timeout boundary
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest = await CreateManifest();
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-25)
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
            .Be(TrainState.InProgress, "metadata within timeout should not be reaped");
    }

    [Test]
    public async Task Run_StaleInProgressMetadata_FeedsIntoDeadLetterPipeline()
    {
        // Arrange — Manifest with maxRetries=0 and one stale InProgress metadata.
        // After the reaper marks it Failed, ReapFailedJobsJunction should dead-letter it
        // in the same ManifestManager cycle.
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var manifest = await CreateManifest(maxRetries: 0);
        var metadata = await CreateMetadata(
            manifest,
            TrainState.InProgress,
            startTime: DateTime.UtcNow.AddMinutes(-60)
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
            .HaveCount(
                1,
                "ReapFailedJobsJunction should dead-letter the manifest in the same cycle"
            );
    }

    [Test]
    public async Task Run_MetadataWithoutManifest_StillReaped()
    {
        // Arrange — InProgress metadata without a manifest (manual/ad-hoc execution)
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "ManualTest" },
                ManifestId = null,
            }
        );
        metadata.TrainState = TrainState.InProgress;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);

        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.StartTime, DateTime.UtcNow.AddMinutes(-60)),
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
            .Be(TrainState.Failed, "orphaned manual in-progress metadata should be reaped too");
    }

    #endregion

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
                Properties = new SchedulerTestInput { Value = "StaleInProgressTest" },
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
                Input = new SchedulerTestInput { Value = "StaleInProgressTest" },
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
