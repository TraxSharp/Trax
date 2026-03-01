using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Scheduler.Tests.Integration.Examples.Workflows;
using Trax.Scheduler.Workflows.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the JobDispatcherWorkflow which picks queued WorkQueue entries
/// and dispatches them as background tasks by creating Metadata records.
/// </summary>
/// <remarks>
/// The JobDispatcherWorkflow runs through the following steps:
/// 1. LoadQueuedJobsStep - Loads all WorkQueue entries with Status == Queued, ordered by group priority, entry priority, then CreatedAt
/// 2. LoadDispatchCapacityStep - Loads global and per-group active counts and limits
/// 3. ApplyCapacityLimitsStep - Filters entries respecting global and per-group capacity limits
/// 4. DispatchJobsStep - For each entry: creates Metadata, updates status to Dispatched, enqueues to BackgroundTaskServer
/// </remarks>
[TestFixture]
public class JobDispatcherWorkflowTests : TestSetup
{
    private IJobDispatcherWorkflow _workflow = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _workflow = Scope.ServiceProvider.GetRequiredService<IJobDispatcherWorkflow>();
    }

    [TearDown]
    public async Task JobDispatcherWorkflowTestsTearDown()
    {
        if (_workflow is IDisposable disposable)
            disposable.Dispose();
    }

    #region LoadQueuedJobsStep Tests

    [Test]
    public async Task Run_WithQueuedEntries_DispatchesThem()
    {
        // Arrange - Create a manifest and a queued work queue entry
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - Entry should be dispatched
        DataContext.Reset();
        var updatedEntry = await DataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);

        updatedEntry.Status.Should().Be(WorkQueueStatus.Dispatched);
        updatedEntry.DispatchedAt.Should().NotBeNull();
    }

    [Test]
    public async Task Run_WithNoQueuedEntries_CompletesWithoutErrors()
    {
        // Arrange - No queued entries

        // Act & Assert
        var act = async () => await _workflow.Run(Unit.Default);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_OnlyPicksQueuedEntries_IgnoresDispatchedAndCancelled()
    {
        // Arrange - Create entries in different statuses.
        // The unique partial index (ix_work_queue_unique_queued_manifest) only allows
        // one Queued entry per manifest, so we must transition entries to non-Queued
        // states before inserting the next one.
        var manifest = await CreateAndSaveManifest();

        var dispatchedEntry = await CreateAndSaveWorkQueueEntry(manifest);
        dispatchedEntry = await DataContext.WorkQueues.FirstAsync(q => q.Id == dispatchedEntry.Id);
        dispatchedEntry.Status = WorkQueueStatus.Dispatched;
        dispatchedEntry.DispatchedAt = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var cancelledEntry = await CreateAndSaveWorkQueueEntry(manifest);
        cancelledEntry = await DataContext.WorkQueues.FirstAsync(q => q.Id == cancelledEntry.Id);
        cancelledEntry.Status = WorkQueueStatus.Cancelled;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Create the queued entry last — only one Queued entry per manifest is allowed
        var queuedEntry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - Only the queued entry should have been dispatched
        DataContext.Reset();

        var updatedQueued = await DataContext.WorkQueues.FirstAsync(q => q.Id == queuedEntry.Id);
        updatedQueued.Status.Should().Be(WorkQueueStatus.Dispatched);

        var updatedDispatched = await DataContext.WorkQueues.FirstAsync(q =>
            q.Id == dispatchedEntry.Id
        );
        updatedDispatched.Status.Should().Be(WorkQueueStatus.Dispatched);

        var updatedCancelled = await DataContext.WorkQueues.FirstAsync(q =>
            q.Id == cancelledEntry.Id
        );
        updatedCancelled.Status.Should().Be(WorkQueueStatus.Cancelled);
    }

    #endregion

    #region DispatchJobsStep Tests

    [Test]
    public async Task Run_CreatesMetadataForQueuedEntry()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - A Metadata record should be created and linked
        DataContext.Reset();
        var updatedEntry = await DataContext
            .WorkQueues.Include(q => q.Metadata)
            .FirstAsync(q => q.Id == entry.Id);

        updatedEntry.MetadataId.Should().NotBeNull();
        updatedEntry.Metadata.Should().NotBeNull();
        updatedEntry.Metadata!.ManifestId.Should().Be(manifest.Id);
    }

    [Test]
    public async Task Run_CreatesMetadataWithCorrectWorkflowName()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act
        await _workflow.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var metadata = await DataContext
            .Metadatas.Where(m => m.ManifestId == manifest.Id)
            .FirstOrDefaultAsync();

        metadata.Should().NotBeNull();
        metadata!.Name.Should().Be(typeof(SchedulerTestWorkflow).FullName);
    }

    [Test]
    public async Task Run_SetsDispatchedAtTimestamp()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);
        var beforeDispatch = DateTime.UtcNow;

        // Act
        await _workflow.Run(Unit.Default);

        // Assert
        DataContext.Reset();
        var updatedEntry = await DataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);

        updatedEntry.DispatchedAt.Should().NotBeNull();
        updatedEntry.DispatchedAt.Should().BeOnOrAfter(beforeDispatch);
        updatedEntry.DispatchedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Run_WithMultipleQueuedEntries_DispatchesAllInOrder()
    {
        // Arrange - Create multiple queued entries for different manifests
        var manifest1 = await CreateAndSaveManifest(inputValue: "First");
        var entry1 = await CreateAndSaveWorkQueueEntry(manifest1);

        var manifest2 = await CreateAndSaveManifest(inputValue: "Second");
        var entry2 = await CreateAndSaveWorkQueueEntry(manifest2);

        var manifest3 = await CreateAndSaveManifest(inputValue: "Third");
        var entry3 = await CreateAndSaveWorkQueueEntry(manifest3);

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - All entries should be dispatched
        DataContext.Reset();
        var entries = await DataContext
            .WorkQueues.Where(q => new[] { entry1.Id, entry2.Id, entry3.Id }.Contains(q.Id))
            .ToListAsync();

        entries.Should().HaveCount(3);
        entries
            .Should()
            .AllSatisfy(e =>
            {
                e.Status.Should().Be(WorkQueueStatus.Dispatched);
                e.MetadataId.Should().NotBeNull();
                e.DispatchedAt.Should().NotBeNull();
            });
    }

    [Test]
    public async Task Run_ExecutesWorkflowViaInMemoryTaskServer()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - InMemoryTaskServer executes immediately, which updates LastSuccessfulRun
        DataContext.Reset();
        var updatedManifest = await DataContext.Manifests.FirstOrDefaultAsync(m =>
            m.Id == manifest.Id
        );

        updatedManifest.Should().NotBeNull();
        updatedManifest!.LastSuccessfulRun.Should().NotBeNull();
        updatedManifest
            .LastSuccessfulRun.Should()
            .BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    #endregion

    #region Priority Tests

    [Test]
    public async Task Run_DispatchesHigherPriorityEntriesFirst()
    {
        // Arrange - Create entries with different priorities
        var lowManifest = await CreateAndSaveManifest(inputValue: "Low");
        var lowEntry = await CreateAndSaveWorkQueueEntry(lowManifest, priority: 0);

        await Task.Delay(50);

        var midManifest = await CreateAndSaveManifest(inputValue: "Mid");
        var midEntry = await CreateAndSaveWorkQueueEntry(midManifest, priority: 15);

        await Task.Delay(50);

        var highManifest = await CreateAndSaveManifest(inputValue: "High");
        var highEntry = await CreateAndSaveWorkQueueEntry(highManifest, priority: 31);

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - All entries should be dispatched
        DataContext.Reset();

        var entries = await DataContext
            .WorkQueues.Where(q => new[] { lowEntry.Id, midEntry.Id, highEntry.Id }.Contains(q.Id))
            .ToListAsync();

        entries.Should().HaveCount(3);
        entries.Should().AllSatisfy(e => e.Status.Should().Be(WorkQueueStatus.Dispatched));

        // Verify dispatch order: higher priority should have earlier MetadataId
        // (since they are dispatched sequentially, earlier dispatch = lower MetadataId)
        var highMeta = entries.First(e => e.Id == highEntry.Id).MetadataId!.Value;
        var midMeta = entries.First(e => e.Id == midEntry.Id).MetadataId!.Value;
        var lowMeta = entries.First(e => e.Id == lowEntry.Id).MetadataId!.Value;

        highMeta
            .Should()
            .BeLessThan(midMeta, "priority 31 should be dispatched before priority 15");
        midMeta.Should().BeLessThan(lowMeta, "priority 15 should be dispatched before priority 0");
    }

    #endregion

    #region Manual Queue Tests (No Manifest)

    [Test]
    public async Task Run_ManualQueueEntry_WithNoManifest_GetsDispatched()
    {
        // Arrange - Create a work queue entry without a manifest (simulates dashboard manual queue)
        var entry = await CreateAndSaveManualWorkQueueEntry();

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - Entry should be dispatched despite having no manifest
        DataContext.Reset();
        var updatedEntry = await DataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);

        updatedEntry.Status.Should().Be(WorkQueueStatus.Dispatched);
        updatedEntry.DispatchedAt.Should().NotBeNull();
    }

    [Test]
    public async Task Run_ManualQueueEntry_CreatesMetadataWithNullManifestId()
    {
        // Arrange
        var entry = await CreateAndSaveManualWorkQueueEntry();

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - Metadata should be created with null ManifestId
        DataContext.Reset();
        var updatedEntry = await DataContext
            .WorkQueues.Include(q => q.Metadata)
            .FirstAsync(q => q.Id == entry.Id);

        updatedEntry.MetadataId.Should().NotBeNull();
        updatedEntry.Metadata.Should().NotBeNull();
        updatedEntry.Metadata!.ManifestId.Should().BeNull();
    }

    [Test]
    public async Task Run_ManualAndManifestEntries_BothGetDispatched()
    {
        // Arrange - One manifest-based entry and one manual entry
        var manifest = await CreateAndSaveManifest();
        var manifestEntry = await CreateAndSaveWorkQueueEntry(manifest);
        var manualEntry = await CreateAndSaveManualWorkQueueEntry(inputValue: "ManualJob");

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - Both should be dispatched
        DataContext.Reset();

        var updatedManifest = await DataContext.WorkQueues.FirstAsync(q =>
            q.Id == manifestEntry.Id
        );
        updatedManifest.Status.Should().Be(WorkQueueStatus.Dispatched);

        var updatedManual = await DataContext.WorkQueues.FirstAsync(q => q.Id == manualEntry.Id);
        updatedManual.Status.Should().Be(WorkQueueStatus.Dispatched);
    }

    [Test]
    public async Task Run_ManualEntry_NotBlockedByDisabledGroup()
    {
        // Arrange - A disabled group entry and a manual entry (no manifest)
        var disabledGroup = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: "disabled-group",
            isEnabled: false
        );
        var disabledManifest = await CreateAndSaveManifestInGroup(disabledGroup);
        var disabledEntry = await CreateAndSaveWorkQueueEntry(disabledManifest);
        var manualEntry = await CreateAndSaveManualWorkQueueEntry(inputValue: "ManualJob");

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - Manual entry dispatches, disabled group entry stays queued
        DataContext.Reset();

        var updatedDisabled = await DataContext.WorkQueues.FirstAsync(q =>
            q.Id == disabledEntry.Id
        );
        updatedDisabled.Status.Should().Be(WorkQueueStatus.Queued);

        var updatedManual = await DataContext.WorkQueues.FirstAsync(q => q.Id == manualEntry.Id);
        updatedManual.Status.Should().Be(WorkQueueStatus.Dispatched);
    }

    [Test]
    public async Task Run_ManualEntry_OrderedByPriorityThenCreatedAt()
    {
        // Arrange - Manual entries with different priorities
        var lowEntry = await CreateAndSaveManualWorkQueueEntry(inputValue: "Low", priority: 0);
        await Task.Delay(50);
        var highEntry = await CreateAndSaveManualWorkQueueEntry(inputValue: "High", priority: 31);

        // Act
        await _workflow.Run(Unit.Default);

        // Assert - Both dispatched, higher priority first (lower MetadataId)
        DataContext.Reset();

        var entries = await DataContext
            .WorkQueues.Where(q => new[] { lowEntry.Id, highEntry.Id }.Contains(q.Id))
            .ToListAsync();

        entries.Should().AllSatisfy(e => e.Status.Should().Be(WorkQueueStatus.Dispatched));

        var highMeta = entries.First(e => e.Id == highEntry.Id).MetadataId!.Value;
        var lowMeta = entries.First(e => e.Id == lowEntry.Id).MetadataId!.Value;

        highMeta
            .Should()
            .BeLessThan(lowMeta, "higher priority manual entry should be dispatched first");
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
                Properties = new SchedulerTestInput { Value = inputValue },
            }
        );
        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<WorkQueue> CreateAndSaveWorkQueueEntry(
        Manifest manifest,
        string? inputValue = null,
        int priority = 0
    )
    {
        var input = inputValue ?? manifest.Properties;
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                WorkflowName = typeof(SchedulerTestWorkflow).FullName!,
                Input = input,
                InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                ManifestId = manifest.Id,
                Priority = priority,
            }
        );

        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return entry;
    }

    /// <summary>
    /// Creates a work queue entry without a ManifestId, simulating a manual queue
    /// from the dashboard or a re-run from the Metadata page.
    /// </summary>
    private async Task<WorkQueue> CreateAndSaveManualWorkQueueEntry(
        string inputValue = "ManualTestValue",
        int priority = 0
    )
    {
        var serializedInput = JsonSerializer.Serialize(
            new SchedulerTestInput { Value = inputValue },
            TraxJsonSerializationOptions.ManifestProperties
        );

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                WorkflowName = typeof(SchedulerTestWorkflow).FullName!,
                Input = serializedInput,
                InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                Priority = priority,
            }
        );

        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return entry;
    }

    private async Task<Manifest> CreateAndSaveManifestInGroup(ManifestGroup group)
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestWorkflow),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "TestValue" },
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
