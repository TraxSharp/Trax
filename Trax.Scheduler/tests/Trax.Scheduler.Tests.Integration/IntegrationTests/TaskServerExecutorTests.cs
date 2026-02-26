using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Workflows.TaskServerExecutor;
using Trax.Core.Exceptions;
using Trax.Scheduler.Tests.Integration.Examples.Workflows;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

[TestFixture]
public class TaskServerExecutorTests : TestSetup
{
    #region Run - Null Metadata Tests

    [Test]
    public async Task Run_WhenMetadataNotFound_ThrowsWorkflowException()
    {
        // Arrange
        var nonExistentMetadataId = 999999;

        // Act
        var act = async () =>
            await TaskServerExecutor.Run(new ExecuteManifestRequest(nonExistentMetadataId));

        // Assert
        await act.Should().ThrowAsync<WorkflowException>().WithMessage("*not found*");
    }

    #endregion

    #region Run - Invalid State Tests

    [Test]
    public async Task Run_WhenStateIsCompleted_ThrowsWorkflowException()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(manifest, WorkflowState.Completed);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        var act = async () =>
            await TaskServerExecutor.Run(new ExecuteManifestRequest(metadata.Id, input));

        // Assert
        await act.Should()
            .ThrowAsync<WorkflowException>()
            .WithMessage("*Cannot execute a job with state Completed*");
    }

    [Test]
    public async Task Run_WhenStateIsFailed_ThrowsWorkflowException()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(manifest, WorkflowState.Failed);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        var act = async () =>
            await TaskServerExecutor.Run(new ExecuteManifestRequest(metadata.Id, input));

        // Assert
        await act.Should()
            .ThrowAsync<WorkflowException>()
            .WithMessage("*Cannot execute a job with state Failed*");
    }

    [Test]
    public async Task Run_WhenStateIsInProgress_ThrowsWorkflowException()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(manifest, WorkflowState.InProgress);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        var act = async () =>
            await TaskServerExecutor.Run(new ExecuteManifestRequest(metadata.Id, input));

        // Assert
        await act.Should()
            .ThrowAsync<WorkflowException>()
            .WithMessage("*Cannot execute a job with state InProgress*");
    }

    #endregion

    #region Run - Null Manifest Tests

    [Test]
    public async Task Run_WhenManifestIsNull_SucceedsButSkipsManifestUpdate()
    {
        // Arrange - Create metadata without a manifest
        var input = new SchedulerTestInput { Value = "Test" };
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestWorkflow).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ManifestId = null
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Should succeed (UpdateManifestSuccessStep gracefully handles null manifest)
        var act = async () =>
            await TaskServerExecutor.Run(new ExecuteManifestRequest(metadata.Id, input));
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Run - Successful Execution Tests

    [Test]
    public async Task Run_WhenStateIsPending_ExecutesWorkflowSuccessfully()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(manifest, WorkflowState.Pending);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        await TaskServerExecutor.Run(new ExecuteManifestRequest(metadata.Id, input));

        // Assert - Verify execution happened (LastSuccessfulRun updated)
        DataContext.Reset();
        var updatedManifest = await DataContext.Manifests.FirstOrDefaultAsync(
            x => x.Id == manifest.Id
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
        var metadata = await CreateAndSaveMetadata(manifest, WorkflowState.Pending);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        await TaskServerExecutor.Run(new ExecuteManifestRequest(metadata.Id, input));
        var afterExecution = DateTime.UtcNow;

        // Assert
        DataContext.Reset();
        var updatedManifest = await DataContext.Manifests.FirstOrDefaultAsync(
            x => x.Id == manifest.Id
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
        var metadata = await CreateAndSaveMetadata(manifest, WorkflowState.Pending);
        var input = manifest.GetProperties<SchedulerTestInput>();

        // Act
        await TaskServerExecutor.Run(new ExecuteManifestRequest(metadata.Id, input));

        // Assert
        DataContext.Reset();
        var updatedManifest = await DataContext.Manifests.FirstOrDefaultAsync(
            x => x.Id == manifest.Id
        );

        updatedManifest.Should().NotBeNull();
        updatedManifest!.LastSuccessfulRun.Should().NotBeNull();
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
                Name = typeof(SchedulerTestWorkflow),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = inputValue }
            }
        );
        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Metadata> CreateAndSaveMetadata(Manifest manifest, WorkflowState state)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestWorkflow).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = manifest.GetProperties<SchedulerTestInput>(),
                ManifestId = manifest.Id
            }
        );

        metadata.WorkflowState = state;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    #endregion
}
