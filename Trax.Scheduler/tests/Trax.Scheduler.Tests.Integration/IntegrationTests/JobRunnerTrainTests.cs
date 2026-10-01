using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The JobRunner loads, validates and runs a scheduled train against a real database, then
/// records its success.
///
/// <para>Enforces <c>docs/adr/0005-a-scheduled-runs-bookkeeping-lives-in-the-junction-that-ran-it.md</c>: a cancellation
/// that lands after the scheduled train completed does not lose the manifest update.</para>
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-a-scheduled-runs-bookkeeping-lives-in-the-junction-that-ran-it.md")]
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class JobRunnerTrainTests : TestSetup
{
    #region Run - Null Metadata Tests

    [Test]
    public async Task Run_WhenMetadataNotFound_ThrowsTrainException()
    {
        // Arrange
        var nonExistentMetadataId = 999999;

        // Act
        var act = async () => await JobRunner.Run(new RunJobRequest(nonExistentMetadataId));

        // Assert
        await act.Should().ThrowAsync<TrainException>().WithMessage("*not found*");
    }

    #endregion

    // A row that is not Pending is covered by DuplicateDeliveryTests: the delivery completes
    // without running the train and records nothing.

    #region Run - Null Manifest Tests

    [Test]
    public async Task Run_WhenManifestIsNull_SucceedsButSkipsManifestUpdate()
    {
        // Arrange - Create metadata without a manifest
        var input = new SchedulerTestInput { Value = "Test" };
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ManifestId = null,
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Should succeed (RunScheduledTrainJunction skips the manifest update when there is none)
        var act = async () => await JobRunner.Run(new RunJobRequest(metadata.Id, input));
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Run - Shutdown Tests

    [Test]
    public async Task Run_CancelledAfterTheScheduledTrainCompletes_StillRecordsTheManifestSuccess()
    {
        // Arrange - a scheduled train that cancels its JobRunner's token as it finishes, the way
        // a shutdown landing between the train completing and the JobRunner's bookkeeping would.
        var key = Guid.NewGuid().ToString("N");
        var input = new CancelsItsRunnerInput { Key = key };
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(CancelsItsRunnerTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                MaxRetries = 3,
                Properties = input,
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(CancelsItsRunnerTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ManifestId = manifest.Id,
            }
        );
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        using var runner = new CancellationTokenSource();
        CancelsItsRunnerTrain.Runners[key] = runner;

        // Act
        try
        {
            await JobRunner.Run(new RunJobRequest(metadata.Id, input), runner.Token);
        }
        catch (OperationCanceledException)
        {
            // Whether the JobRunner run itself ends cancelled is not what this test is about.
        }
        finally
        {
            CancelsItsRunnerTrain.Runners.TryRemove(key, out _);
        }

        // Assert - the scheduled work completed, so the manifest records it: a lost update
        // leaves LastSuccessfulRun stale and a Once manifest enabled to run again.
        runner.IsCancellationRequested.Should().BeTrue("the scheduled train cancelled it");
        DataContext.Reset();
        var updated = await DataContext.Manifests.SingleAsync(x => x.Id == manifest.Id);
        updated
            .LastSuccessfulRun.Should()
            .NotBeNull(
                "the scheduled train completed before the cancellation, so its bookkeeping must "
                    + "not be skipped. See docs/adr/0005-a-scheduled-runs-bookkeeping-lives-in-the-junction-that-ran-it.md"
            );
        updated.IsEnabled.Should().BeFalse("a Once manifest is disabled once it has succeeded");
    }

    #endregion

    #region Run - Successful Execution Tests

    [Test]
    public async Task Run_WhenStateIsPending_ExecutesTrainSuccessfully()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(manifest, TrainState.Pending);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        await JobRunner.Run(new RunJobRequest(metadata.Id, input));

        // Assert - Verify execution happened (LastSuccessfulRun updated)
        DataContext.Reset();
        var updatedManifest = await DataContext.Manifests.FirstOrDefaultAsync(x =>
            x.Id == manifest.Id
        );

        updatedManifest.Should().NotBeNull();
        updatedManifest!.LastSuccessfulRun.Should().NotBeNull();
        updatedManifest
            .LastSuccessfulRun.Should()
            .BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Run_WhenSuccessful_UpdatesLastSuccessfulRunOnManifest()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var beforeExecution = DateTime.UtcNow;
        var metadata = await CreateAndSaveMetadata(manifest, TrainState.Pending);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        await JobRunner.Run(new RunJobRequest(metadata.Id, input));
        var afterExecution = DateTime.UtcNow;

        // Assert
        DataContext.Reset();
        var updatedManifest = await DataContext.Manifests.FirstOrDefaultAsync(x =>
            x.Id == manifest.Id
        );

        updatedManifest.Should().NotBeNull();
        updatedManifest!.LastSuccessfulRun.Should().NotBeNull();
        updatedManifest.LastSuccessfulRun.Should().BeOnOrAfter(beforeExecution);
        updatedManifest.LastSuccessfulRun.Should().BeOnOrBefore(afterExecution.AddSeconds(1));
    }

    [Test]
    public async Task Run_WithDifferentInputValues_ExecutesCorrectly()
    {
        // Arrange
        var testValue = $"UniqueValue_{Guid.NewGuid():N}";
        var manifest = await CreateAndSaveManifest(testValue);
        var metadata = await CreateAndSaveMetadata(manifest, TrainState.Pending);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        await JobRunner.Run(new RunJobRequest(metadata.Id, input));

        // Assert
        DataContext.Reset();
        var updatedManifest = await DataContext.Manifests.FirstOrDefaultAsync(x =>
            x.Id == manifest.Id
        );

        updatedManifest.Should().NotBeNull();
        updatedManifest!.LastSuccessfulRun.Should().NotBeNull();
    }

    #endregion

    #region Run - Input Belongs To Another Train

    [Test]
    public async Task Run_WhenInputBelongsToAnotherTrain_IsRefusedAndTheRowStaysPending()
    {
        // Arrange: a Pending row of SchedulerTestTrain, given the failing train's input.
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(manifest, TrainState.Pending);
        var otherInput = new FailingSchedulerTestInput { FailureMessage = "should not run" };

        // Act
        var act = async () => await JobRunner.Run(new RunJobRequest(metadata.Id, otherInput));

        // Assert
        await act.Should().ThrowAsync<TrainException>().WithMessage("*belongs to train*");

        DataContext.Reset();
        var row = await DataContext.Metadatas.AsNoTracking().FirstAsync(x => x.Id == metadata.Id);
        row.TrainState.Should()
            .Be(
                TrainState.Pending,
                "a row is run only with its own train's input, and a refusal leaves it untouched (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
        var reloaded = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(x => x.Id == manifest.Id);
        reloaded.LastSuccessfulRun.Should().BeNull();
    }

    [Test]
    public async Task Run_WhenRowNamesTheTrainByItsInterface_Runs()
    {
        var manifest = await CreateAndSaveManifest();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ISchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = manifest.GetProperties<SchedulerTestInput>(),
                ManifestId = manifest.Id,
            }
        );
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var act = async () =>
            await JobRunner.Run(
                new RunJobRequest(metadata.Id, manifest.GetProperties<SchedulerTestInput>())
            );

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_WhenRowNamesTheInterfaceButTheInputIsAnotherTrains_IsRefusedAndTheRowStaysPending()
    {
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(
            manifest,
            TrainState.Pending,
            name: typeof(ISchedulerTestTrain).FullName!
        );

        var act = async () =>
            await JobRunner.Run(
                new RunJobRequest(
                    metadata.Id,
                    new FailingSchedulerTestInput { FailureMessage = "should not run" }
                )
            );

        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("*not a registered train taking the input given*");
        DataContext.Reset();
        (await DataContext.Metadatas.AsNoTracking().FirstAsync(x => x.Id == metadata.Id))
            .TrainState.Should()
            .Be(TrainState.Pending, "the canonical name does not excuse another train's input");
    }

    [Test]
    public async Task Run_WhenRowNamesTheTrainByItsInterfaceShortName_Runs()
    {
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(
            manifest,
            TrainState.Pending,
            name: nameof(ISchedulerTestTrain)
        );

        await JobRunner.Run(
            new RunJobRequest(metadata.Id, manifest.GetProperties<SchedulerTestInput>())
        );

        DataContext.Reset();
        (await DataContext.Metadatas.AsNoTracking().FirstAsync(x => x.Id == metadata.Id))
            .TrainState.Should()
            .Be(TrainState.Completed, "the interface's short name is the wire's fallback name");
    }

    #endregion

    #region Helper Methods

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
                ScheduleType = ScheduleType.None,
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

    private async Task<Metadata> CreateAndSaveMetadata(
        Manifest manifest,
        TrainState state,
        string? name = null
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name ?? typeof(SchedulerTestTrain).FullName!,
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

    #endregion
}
