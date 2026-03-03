using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Examples.Trains;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

[TestFixture]
public class TraxSchedulerCancelTests : TestSetup
{
    private ITraxScheduler _scheduler = null!;
    private ICancellationRegistry _registry = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        _registry = Scope.ServiceProvider.GetRequiredService<ICancellationRegistry>();
    }

    #region CancelAsync Tests

    [Test]
    public async Task CancelAsync_WithInProgressMetadata_SetsCancellationRequested()
    {
        // Arrange
        var manifest = await CreateManifestWithMetadata(TrainState.InProgress);

        // Act
        var count = await _scheduler.CancelAsync(manifest.ExternalId);

        // Assert
        count.Should().Be(1);

        DataContext.Reset();
        var metadata = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.ManifestId == manifest.Id);
        metadata.CancellationRequested.Should().BeTrue();
    }

    [Test]
    public async Task CancelAsync_WithNoInProgressMetadata_ReturnsZero()
    {
        // Arrange
        var manifest = await CreateManifestWithMetadata(TrainState.Completed);

        // Act
        var count = await _scheduler.CancelAsync(manifest.ExternalId);

        // Assert
        count.Should().Be(0);
    }

    [Test]
    public async Task CancelAsync_WithMultipleInProgress_CancelsAll()
    {
        // Arrange
        var (manifest, _) = await CreateManifestWithMultipleMetadata(
            TrainState.InProgress,
            TrainState.InProgress,
            TrainState.Completed
        );

        // Act
        var count = await _scheduler.CancelAsync(manifest.ExternalId);

        // Assert
        count.Should().Be(2);

        DataContext.Reset();
        var inProgressMetadata = await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId == manifest.Id && m.TrainState == TrainState.InProgress)
            .ToListAsync();
        inProgressMetadata.Should().AllSatisfy(m => m.CancellationRequested.Should().BeTrue());
    }

    [Test]
    public async Task CancelAsync_NonexistentExternalId_ThrowsInvalidOperation()
    {
        // Act
        var act = () => _scheduler.CancelAsync("nonexistent-id");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task CancelAsync_CallsRegistryTryCancel()
    {
        // Arrange
        var manifest = await CreateManifestWithMetadata(TrainState.InProgress);

        DataContext.Reset();
        var metadata = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.ManifestId == manifest.Id);

        // Register a CTS so we can verify TryCancel is called
        using var cts = new CancellationTokenSource();
        _registry.Register(metadata.Id, cts);

        try
        {
            // Act
            await _scheduler.CancelAsync(manifest.ExternalId);

            // Assert — the CTS should have been cancelled via the registry
            cts.IsCancellationRequested.Should().BeTrue();
        }
        finally
        {
            _registry.Unregister(metadata.Id);
        }
    }

    #endregion

    #region CancelGroupAsync Tests

    [Test]
    public async Task CancelGroupAsync_CancelsAllInProgressInGroup()
    {
        // Arrange — two manifests in the same group, both with InProgress metadata
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"cancel-group-{Guid.NewGuid():N}"
        );

        var manifest1 = await CreateManifestInGroup(group);
        var manifest2 = await CreateManifestInGroup(group);

        await CreateMetadataForManifest(manifest1, TrainState.InProgress);
        await CreateMetadataForManifest(manifest2, TrainState.InProgress);

        // Act
        var count = await _scheduler.CancelGroupAsync(group.Id);

        // Assert
        count.Should().Be(2);

        DataContext.Reset();
        var allMetadata = await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId == manifest1.Id || m.ManifestId == manifest2.Id)
            .ToListAsync();
        allMetadata.Should().AllSatisfy(m => m.CancellationRequested.Should().BeTrue());
    }

    [Test]
    public async Task CancelGroupAsync_EmptyGroup_ReturnsZero()
    {
        // Arrange
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"empty-group-{Guid.NewGuid():N}"
        );

        // Act
        var count = await _scheduler.CancelGroupAsync(group.Id);

        // Assert
        count.Should().Be(0);
    }

    [Test]
    public async Task CancelGroupAsync_OnlyCancelsInProgress_NotCompletedOrFailed()
    {
        // Arrange
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"mixed-group-{Guid.NewGuid():N}"
        );

        var manifest = await CreateManifestInGroup(group);
        await CreateMetadataForManifest(manifest, TrainState.InProgress);
        await CreateMetadataForManifest(manifest, TrainState.Completed);
        await CreateMetadataForManifest(manifest, TrainState.Failed);

        // Act
        var count = await _scheduler.CancelGroupAsync(group.Id);

        // Assert
        count.Should().Be(1);

        DataContext.Reset();
        var completedMeta = await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId == manifest.Id && m.TrainState == TrainState.Completed)
            .FirstAsync();
        completedMeta.CancellationRequested.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private async Task<Manifest> CreateManifestWithMetadata(TrainState state)
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
                Properties = new SchedulerTestInput { Value = "CancelTest" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await CreateMetadataForManifest(manifest, state);

        return manifest;
    }

    private async Task<(Manifest, List<Metadata>)> CreateManifestWithMultipleMetadata(
        params TrainState[] states
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
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "CancelTest" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var metadatas = new List<Metadata>();
        foreach (var state in states)
            metadatas.Add(await CreateMetadataForManifest(manifest, state));

        return (manifest, metadatas);
    }

    private async Task<Manifest> CreateManifestInGroup(ManifestGroup group)
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "GroupCancelTest" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task<Metadata> CreateMetadataForManifest(Manifest manifest, TrainState state)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "CancelTest" },
                ManifestId = manifest.Id,
            }
        );
        metadata.TrainState = state;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return metadata;
    }

    #endregion
}
