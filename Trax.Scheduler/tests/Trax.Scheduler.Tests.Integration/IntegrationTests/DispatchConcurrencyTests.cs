using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Tests.Integration.Examples.Workflows;
using Trax.Scheduler.Workflows.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Concurrency tests for the JobDispatcher's <c>FOR UPDATE SKIP LOCKED</c> dispatch pattern.
/// Verifies that concurrent dispatch cycles do not create duplicate Metadata records
/// or dispatch the same work queue entry twice.
/// </summary>
[TestFixture]
public class DispatchConcurrencyTests : TestSetup
{
    [Test]
    public async Task ConcurrentDispatch_SameEntry_OnlyOneMetadataCreated()
    {
        // Arrange - Create a single queued entry
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act - Run two dispatchers concurrently against the same entry
        var task1 = RunDispatcherInScope();
        var task2 = RunDispatcherInScope();
        await Task.WhenAll(task1, task2);

        // Assert - Exactly one Metadata record should exist for this manifest
        using var assertScope = Scope
            .ServiceProvider.GetRequiredService<IServiceProvider>()
            .CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<IDataContext>();

        var metadataCount = await assertContext.Metadatas.CountAsync(m =>
            m.ManifestId == manifest.Id
        );
        metadataCount.Should().Be(1, "FOR UPDATE SKIP LOCKED should prevent duplicate dispatch");

        var updatedEntry = await assertContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
        updatedEntry.Status.Should().Be(WorkQueueStatus.Dispatched);
        updatedEntry.MetadataId.Should().NotBeNull();
    }

    [Test]
    public async Task ConcurrentDispatch_MultipleEntries_EachDispatchedExactlyOnce()
    {
        // Arrange - Create multiple entries for different manifests
        var entries = new List<(Manifest manifest, WorkQueue entry)>();
        for (var i = 0; i < 5; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Concurrent_{i}");
            var entry = await CreateAndSaveWorkQueueEntry(manifest);
            entries.Add((manifest, entry));
        }

        // Act - Run three dispatchers concurrently
        var task1 = RunDispatcherInScope();
        var task2 = RunDispatcherInScope();
        var task3 = RunDispatcherInScope();
        await Task.WhenAll(task1, task2, task3);

        // Assert - Each entry should have exactly one Metadata record
        using var assertScope = Scope
            .ServiceProvider.GetRequiredService<IServiceProvider>()
            .CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<IDataContext>();

        foreach (var (manifest, entry) in entries)
        {
            var metadataCount = await assertContext.Metadatas.CountAsync(m =>
                m.ManifestId == manifest.Id
            );
            metadataCount
                .Should()
                .Be(
                    1,
                    $"manifest {manifest.Id} should have exactly one Metadata (concurrent safe dispatch)"
                );

            var updatedEntry = await assertContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            updatedEntry.Status.Should().Be(WorkQueueStatus.Dispatched);
        }
    }

    [Test]
    public async Task ConcurrentDispatch_ClaimedEntrySkippedGracefully()
    {
        // Arrange - Single entry that will be contested
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act - Run multiple dispatchers; the losers should skip without error
        var tasks = Enumerable.Range(0, 4).Select(_ => RunDispatcherInScope()).ToArray();
        var act = async () => await Task.WhenAll(tasks);

        // Assert - No exceptions thrown
        await act.Should().NotThrowAsync("losing dispatchers should skip gracefully");

        // And exactly one dispatch occurred
        using var assertScope = Scope
            .ServiceProvider.GetRequiredService<IServiceProvider>()
            .CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<IDataContext>();

        var metadataCount = await assertContext.Metadatas.CountAsync(m =>
            m.ManifestId == manifest.Id
        );
        metadataCount.Should().Be(1);
    }

    [Test]
    public async Task UniquePartialIndex_PreventsDirectDuplicateQueuedEntries()
    {
        // Arrange - Create a manifest and one queued entry
        var manifest = await CreateAndSaveManifest();
        await CreateAndSaveWorkQueueEntry(manifest);

        // Act - Try to insert a second queued entry for the same manifest
        var act = async () =>
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    WorkflowName = typeof(SchedulerTestWorkflow).FullName!,
                    Input = manifest.Properties,
                    InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                    ManifestId = manifest.Id,
                }
            );
            await DataContext.Track(entry);
            await DataContext.SaveChanges(CancellationToken.None);
        };

        // Assert - Unique partial index should prevent the duplicate
        await act.Should()
            .ThrowAsync<DbUpdateException>()
            .WithInnerException<DbUpdateException, Npgsql.PostgresException>()
            .Where(
                e => e.SqlState == "23505",
                "unique partial index ix_work_queue_unique_queued_manifest should enforce uniqueness"
            );
    }

    [Test]
    public async Task UniquePartialIndex_AllowsMultipleManualEntries()
    {
        // Arrange & Act - Create multiple manual entries (null ManifestId)
        // The unique partial index has WHERE manifest_id IS NOT NULL, so these are excluded
        var act = async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                var entry = WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        WorkflowName = typeof(SchedulerTestWorkflow).FullName!,
                        Input = "{}",
                        InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                    }
                );
                await DataContext.Track(entry);
                await DataContext.SaveChanges(CancellationToken.None);
                DataContext.Reset();
            }
        };

        // Assert - No constraint violation for null ManifestId entries
        await act.Should()
            .NotThrowAsync("manual entries (null ManifestId) are excluded from the unique index");
    }

    [Test]
    public async Task UniquePartialIndex_AllowsNewQueuedEntry_AfterPreviousDispatched()
    {
        // Arrange - Create and dispatch an entry
        var manifest = await CreateAndSaveManifest();
        var firstEntry = await CreateAndSaveWorkQueueEntry(manifest);

        // Transition to Dispatched
        firstEntry = await DataContext.WorkQueues.FirstAsync(q => q.Id == firstEntry.Id);
        firstEntry.Status = WorkQueueStatus.Dispatched;
        firstEntry.DispatchedAt = DateTime.UtcNow;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Create a new Queued entry for the same manifest
        var act = async () => await CreateAndSaveWorkQueueEntry(manifest);

        // Assert - Should succeed since the first entry is no longer Queued
        await act.Should()
            .NotThrowAsync(
                "only one Queued entry per manifest is restricted; Dispatched entries don't count"
            );
    }

    #region Helper Methods

    private async Task RunDispatcherInScope()
    {
        using var scope = Scope
            .ServiceProvider.GetRequiredService<IServiceProvider>()
            .CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IJobDispatcherWorkflow>();

        try
        {
            await workflow.Run(Unit.Default);
        }
        finally
        {
            if (workflow is IDisposable disposable)
                disposable.Dispose();
        }
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

    private async Task<WorkQueue> CreateAndSaveWorkQueueEntry(Manifest manifest, int priority = 0)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                WorkflowName = typeof(SchedulerTestWorkflow).FullName!,
                Input = manifest.Properties,
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

    #endregion
}
