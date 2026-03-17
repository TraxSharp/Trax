using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="DormantDependentContext"/> which enables parent trains
/// to selectively activate dormant dependent manifests with runtime-determined input.
/// </summary>
[TestFixture]
public class DormantDependentContextTests : TestSetup
{
    private DormantDependentContext _context = null!;
    private SchedulerConfiguration _schedulerConfig = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _context = Scope.ServiceProvider.GetRequiredService<DormantDependentContext>();
        _schedulerConfig = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
    }

    #region Happy Path Tests

    [Test]
    public async Task ActivateAsync_WhenValid_CreatesWorkQueueEntryWithRuntimeInput()
    {
        // Arrange
        var (parent, dormant) = await CreateParentAndDormantDependent();
        _context.Initialize(parent.Id);

        var runtimeInput = new SchedulerTestInput { Value = "RuntimeValue" };

        // Act
        await _context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            dormant.ExternalId,
            runtimeInput
        );

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dormant.Id)
            .ToListAsync();

        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.Status.Should().Be(WorkQueueStatus.Queued);
        entry.TrainName.Should().Be(dormant.Name);
        entry.InputTypeName.Should().Be(typeof(SchedulerTestInput).FullName);
        entry.Input.Should().Contain("RuntimeValue");
    }

    [Test]
    public async Task ActivateAsync_WorkQueueEntry_HasDependentPriorityBoost()
    {
        // Arrange
        var groupPriority = 5;
        var (parent, dormant) = await CreateParentAndDormantDependent(groupPriority: groupPriority);
        _context.Initialize(parent.Id);

        var expectedPriority = groupPriority + _schedulerConfig.DependentPriorityBoost;

        // Act
        await _context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            dormant.ExternalId,
            new SchedulerTestInput { Value = "PriorityTest" }
        );

        // Assert
        DataContext.Reset();
        var entry = await DataContext.WorkQueues.FirstAsync(q => q.ManifestId == dormant.Id);

        entry.Priority.Should().Be(expectedPriority);
    }

    [Test]
    public async Task ActivateManyAsync_CreatesMultipleWorkQueueEntries()
    {
        // Arrange
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var parent = await CreateAndSaveManifest(group, ScheduleType.Interval, intervalSeconds: 60);
        var dormant1 = await CreateAndSaveDormantDependent(group, parent, "dormant-1");
        var dormant2 = await CreateAndSaveDormantDependent(group, parent, "dormant-2");
        var dormant3 = await CreateAndSaveDormantDependent(group, parent, "dormant-3");
        _context.Initialize(parent.Id);

        var activations = new[]
        {
            (dormant1.ExternalId, new SchedulerTestInput { Value = "Input1" }),
            (dormant2.ExternalId, new SchedulerTestInput { Value = "Input2" }),
            (dormant3.ExternalId, new SchedulerTestInput { Value = "Input3" }),
        };

        // Act
        await _context.ActivateManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            activations
        );

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q =>
                q.ManifestId == dormant1.Id
                || q.ManifestId == dormant2.Id
                || q.ManifestId == dormant3.Id
            )
            .ToListAsync();

        entries.Should().HaveCount(3);
        entries.Select(e => e.Input).Should().Contain(i => i!.Contains("Input1"));
        entries.Select(e => e.Input).Should().Contain(i => i!.Contains("Input2"));
        entries.Select(e => e.Input).Should().Contain(i => i!.Contains("Input3"));
    }

    [Test]
    public async Task ActivateManyAsync_WhenEmpty_ReturnsWithoutAction()
    {
        // Arrange
        var (parent, _) = await CreateParentAndDormantDependent();
        _context.Initialize(parent.Id);

        var activations = Enumerable.Empty<(string ExternalId, SchedulerTestInput Input)>();

        // Act
        await _context.ActivateManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            activations
        );

        // Assert
        DataContext.Reset();
        var entries = await DataContext.WorkQueues.ToListAsync();
        entries.Should().BeEmpty();
    }

    #endregion

    #region Validation Error Tests

    [Test]
    public async Task ActivateAsync_WhenNotInitialized_SkipsWithoutError()
    {
        // Act — context not initialized, should no-op (not throw)
        await _context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "any-id",
            new SchedulerTestInput { Value = "test" }
        );

        // Assert — no work queue entries created
        DataContext.Reset();
        var entries = await DataContext.WorkQueues.ToListAsync();
        entries.Should().BeEmpty();
    }

    [Test]
    public async Task ActivateManyAsync_WhenNotInitialized_SkipsWithoutError()
    {
        // Arrange
        var activations = new[]
        {
            ("dormant-1", new SchedulerTestInput { Value = "Input1" }),
            ("dormant-2", new SchedulerTestInput { Value = "Input2" }),
        };

        // Act — context not initialized, should no-op (not throw)
        await _context.ActivateManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            activations
        );

        // Assert — no work queue entries created
        DataContext.Reset();
        var entries = await DataContext.WorkQueues.ToListAsync();
        entries.Should().BeEmpty();
    }

    [Test]
    public async Task ActivateAsync_WhenManifestNotFound_ThrowsInvalidOperation()
    {
        // Arrange
        var (parent, _) = await CreateParentAndDormantDependent();
        _context.Initialize(parent.Id);

        // Act & Assert
        var act = () =>
            _context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "nonexistent-id",
                new SchedulerTestInput { Value = "test" }
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*No manifest found*nonexistent-id*");
    }

    [Test]
    public async Task ActivateAsync_WhenNotDormantDependent_ThrowsInvalidOperation()
    {
        // Arrange — create a normal Dependent (not DormantDependent)
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var parent = await CreateAndSaveManifest(group, ScheduleType.Interval, intervalSeconds: 60);

        var dependent = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Dependent,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "Dependent" },
                DependsOnManifestId = parent.Id,
            }
        );
        dependent.ManifestGroupId = group.Id;
        await DataContext.Track(dependent);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        _context.Initialize(parent.Id);

        // Act & Assert
        var act = () =>
            _context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                dependent.ExternalId,
                new SchedulerTestInput { Value = "test" }
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected DormantDependent*");
    }

    [Test]
    public async Task ActivateAsync_WhenWrongParent_ThrowsInvalidOperation()
    {
        // Arrange — dormant depends on parent A, but context initialized with parent B
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var parentA = await CreateAndSaveManifest(
            group,
            ScheduleType.Interval,
            intervalSeconds: 60
        );
        var parentB = await CreateAndSaveManifest(
            group,
            ScheduleType.Interval,
            intervalSeconds: 60
        );
        var dormant = await CreateAndSaveDormantDependent(group, parentA, "wrong-parent-test");

        _context.Initialize(parentB.Id); // Wrong parent!

        // Act & Assert
        var act = () =>
            _context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                dormant.ExternalId,
                new SchedulerTestInput { Value = "test" }
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*can only be activated by its declared parent*");
    }

    #endregion

    #region Concurrency Guard Tests

    [Test]
    public async Task ActivateAsync_WhenAlreadyQueued_SkipsWithoutError()
    {
        // Arrange
        var (parent, dormant) = await CreateParentAndDormantDependent();
        _context.Initialize(parent.Id);

        // Create an existing queued entry
        var existingEntry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = dormant.Name,
                Input = dormant.Properties,
                InputTypeName = dormant.PropertyTypeName,
                ManifestId = dormant.Id,
            }
        );
        await DataContext.Track(existingEntry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act — should not throw, just skip
        await _context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            dormant.ExternalId,
            new SchedulerTestInput { Value = "ShouldBeSkipped" }
        );

        // Assert — still only the original entry, no new one
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dormant.Id)
            .ToListAsync();

        entries.Should().HaveCount(1);
        entries[0].Input.Should().NotContain("ShouldBeSkipped");
    }

    [Test]
    public async Task ActivateAsync_WhenActiveExecution_SkipsWithoutError()
    {
        // Arrange
        var (parent, dormant) = await CreateParentAndDormantDependent();
        _context.Initialize(parent.Id);

        // Create an in-progress metadata record
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = dormant.Name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = dormant.Id,
            }
        );
        metadata.TrainState = TrainState.InProgress;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act — should not throw, just skip
        await _context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            dormant.ExternalId,
            new SchedulerTestInput { Value = "ShouldBeSkipped" }
        );

        // Assert — no work queue entry created
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dormant.Id)
            .ToListAsync();

        entries.Should().BeEmpty();
    }

    #endregion

    #region Cross-Scope Tests (AsyncLocal Regression)

    [Test]
    public async Task ActivateAsync_WhenInitializedInParentScope_WorksFromChildScope()
    {
        // Regression test: DormantDependentContext.Initialize is called in the JobRunner's
        // DI scope, but TrainBus.RunAsync creates a child scope for the user train. Junctions
        // in that child scope resolve a NEW DormantDependentContext instance. Without AsyncLocal,
        // that instance would be uninitialized and throw/skip.
        //
        // This test reproduces the exact scope topology:
        //   Parent scope: Initialize(parentManifestId)
        //   Child scope:  IDormantDependentContext.ActivateAsync(...)

        // Arrange
        var (parent, dormant) = await CreateParentAndDormantDependent();

        // Initialize in the PARENT scope (like RunScheduledTrainJunction does)
        _context.Initialize(parent.Id);

        // Act — resolve from a CHILD scope (like TrainBus.RunAsync does)
        using var childScope = Scope.ServiceProvider.CreateScope();
        var childContext =
            childScope.ServiceProvider.GetRequiredService<IDormantDependentContext>();

        // The child instance should NOT be the same object as the parent instance
        childContext.Should().NotBeSameAs(_context);

        await childContext.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            dormant.ExternalId,
            new SchedulerTestInput { Value = "CrossScopeValue" }
        );

        // Assert — work queue entry created successfully from the child scope
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dormant.Id)
            .ToListAsync();

        entries.Should().HaveCount(1);
        entries[0].Input.Should().Contain("CrossScopeValue");
    }

    [Test]
    public async Task ActivateManyAsync_WhenInitializedInParentScope_WorksFromChildScope()
    {
        // Same regression test as above, but for the batch activation path.

        // Arrange
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var parent = await CreateAndSaveManifest(group, ScheduleType.Interval, intervalSeconds: 60);
        var dormant1 = await CreateAndSaveDormantDependent(group, parent, "cross-scope-1");
        var dormant2 = await CreateAndSaveDormantDependent(group, parent, "cross-scope-2");

        // Initialize in parent scope
        _context.Initialize(parent.Id);

        // Act — activate from child scope
        using var childScope = Scope.ServiceProvider.CreateScope();
        var childContext =
            childScope.ServiceProvider.GetRequiredService<IDormantDependentContext>();

        await childContext.ActivateManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            new[]
            {
                (dormant1.ExternalId, new SchedulerTestInput { Value = "Batch1" }),
                (dormant2.ExternalId, new SchedulerTestInput { Value = "Batch2" }),
            }
        );

        // Assert
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dormant1.Id || q.ManifestId == dormant2.Id)
            .ToListAsync();

        entries.Should().HaveCount(2);
    }

    [Test]
    public async Task ActivateAsync_AfterReset_SkipsWithoutError()
    {
        // Verify that Reset() clears the AsyncLocal, preventing stale manifest IDs
        // from leaking into subsequent job executions on the same worker task.
        // This is what RunScheduledTrainJunction does in its finally block.

        // Arrange
        var (parent, dormant) = await CreateParentAndDormantDependent();
        _context.Initialize(parent.Id);
        _context.Reset();

        // Act — after Reset, activation should be a no-op (not initialized)
        using var childScope = Scope.ServiceProvider.CreateScope();
        var childContext =
            childScope.ServiceProvider.GetRequiredService<IDormantDependentContext>();

        await childContext.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            dormant.ExternalId,
            new SchedulerTestInput { Value = "ShouldNotActivate" }
        );

        // Assert — no work queue entry created
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => q.ManifestId == dormant.Id)
            .ToListAsync();

        entries.Should().BeEmpty();
    }

    #endregion

    #region Transaction Behavior Tests

    [Test]
    public async Task ActivateManyAsync_WhenOneValidationFails_RollsBackAll()
    {
        // Arrange
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var parentA = await CreateAndSaveManifest(
            group,
            ScheduleType.Interval,
            intervalSeconds: 60
        );
        var parentB = await CreateAndSaveManifest(
            group,
            ScheduleType.Interval,
            intervalSeconds: 60
        );
        var dormantValid = await CreateAndSaveDormantDependent(group, parentA, "valid-dormant");
        var dormantWrongParent = await CreateAndSaveDormantDependent(
            group,
            parentB,
            "wrong-parent-dormant"
        );

        _context.Initialize(parentA.Id);

        var activations = new[]
        {
            (dormantValid.ExternalId, new SchedulerTestInput { Value = "Valid" }),
            (dormantWrongParent.ExternalId, new SchedulerTestInput { Value = "Invalid" }),
        };

        // Act & Assert — the second activation should fail validation
        var act = () =>
            _context.ActivateManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(activations);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert — no entries created (transaction rolled back)
        DataContext.Reset();
        var entries = await DataContext.WorkQueues.ToListAsync();
        entries.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private async Task<(Manifest Parent, Manifest Dormant)> CreateParentAndDormantDependent(
        int groupPriority = 0
    )
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}",
            priority: groupPriority
        );
        var parent = await CreateAndSaveManifest(group, ScheduleType.Interval, intervalSeconds: 60);
        var dormant = await CreateAndSaveDormantDependent(
            group,
            parent,
            $"dormant-{Guid.NewGuid():N}"
        );
        return (parent, dormant);
    }

    private async Task<Manifest> CreateAndSaveManifest(
        Trax.Effect.Models.ManifestGroup.ManifestGroup group,
        ScheduleType scheduleType,
        int? intervalSeconds = null
    )
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = scheduleType,
                IntervalSeconds = intervalSeconds,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "ParentInput" },
            }
        );

        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Manifest> CreateAndSaveDormantDependent(
        Trax.Effect.Models.ManifestGroup.ManifestGroup group,
        Manifest parent,
        string externalId
    )
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.DormantDependent,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "DefaultDormantInput" },
                DependsOnManifestId = parent.Id,
            }
        );

        manifest.ManifestGroupId = group.Id;
        manifest.ExternalId = externalId;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    #endregion
}
