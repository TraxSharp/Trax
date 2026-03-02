using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Tests.Integration.Examples.Trains;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Concurrency tests for the ManifestManager's advisory lock pattern.
/// Verifies that concurrent ManifestManager cycles don't create duplicate WorkQueue entries,
/// and that the advisory lock correctly serializes access.
/// </summary>
/// <remarks>
/// Since tests use PostgreSQL (not InMemory), the advisory lock path is exercised.
/// The ManifestManager wraps its entire train cycle in a transaction with
/// <c>pg_try_advisory_xact_lock</c>, ensuring only one server evaluates manifests per cycle.
/// </remarks>
[TestFixture]
public class ManifestManagerConcurrencyTests : TestSetup
{
    [Test]
    public async Task ConcurrentManifestManager_DueManifest_OnlyOneWorkQueueEntry()
    {
        // Arrange - Create a manifest that is due for execution
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
                IntervalSeconds = 300, // 5 minutes
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "ConcurrencyTest" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        // Set LastSuccessfulRun far in the past so it's due
        manifest.LastSuccessfulRun = DateTime.UtcNow.AddHours(-1);

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Run two ManifestManager trains concurrently.
        // The advisory lock ensures only one actually runs; the other skips.
        // (In tests, the DataContext is a DbContext, so the advisory lock path is taken.)
        var task1 = RunManifestManagerInScope();
        var task2 = RunManifestManagerInScope();
        await Task.WhenAll(task1, task2);

        // Assert - Exactly one WorkQueue entry should exist
        DataContext.Reset();
        var workQueueCount = await DataContext.WorkQueues.CountAsync(q =>
            q.ManifestId == manifest.Id
        );
        workQueueCount
            .Should()
            .Be(
                1,
                "advisory lock should prevent duplicate WorkQueue entries from concurrent cycles"
            );
    }

    [Test]
    public async Task ConcurrentManifestManager_MultipleDueManifests_NoDuplicates()
    {
        // Arrange - Create multiple manifests that are all due
        var manifests = new List<Manifest>();
        for (var i = 0; i < 3; i++)
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
                    IntervalSeconds = 300,
                    MaxRetries = 3,
                    Properties = new SchedulerTestInput { Value = $"ConcurrencyTest_{i}" },
                }
            );
            manifest.ManifestGroupId = group.Id;
            manifest.LastSuccessfulRun = DateTime.UtcNow.AddHours(-1);

            await DataContext.Track(manifest);
            await DataContext.SaveChanges(CancellationToken.None);
            DataContext.Reset();

            manifests.Add(manifest);
        }

        // Act - Run three concurrent ManifestManager cycles
        var task1 = RunManifestManagerInScope();
        var task2 = RunManifestManagerInScope();
        var task3 = RunManifestManagerInScope();
        await Task.WhenAll(task1, task2, task3);

        // Assert - Each manifest should have exactly one WorkQueue entry
        DataContext.Reset();
        foreach (var manifest in manifests)
        {
            var workQueueCount = await DataContext.WorkQueues.CountAsync(q =>
                q.ManifestId == manifest.Id
            );
            workQueueCount
                .Should()
                .Be(
                    1,
                    $"manifest {manifest.Id} should have exactly one WorkQueue entry despite concurrent cycles"
                );
        }
    }

    [Test]
    public async Task ConcurrentManifestManager_NoExceptions()
    {
        // Arrange - Create a due manifest
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
                IntervalSeconds = 300,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "NoExceptionTest" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.LastSuccessfulRun = DateTime.UtcNow.AddHours(-1);

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Run concurrent cycles; losing cycles should complete without error
        var tasks = Enumerable.Range(0, 4).Select(_ => RunManifestManagerInScope()).ToArray();
        var act = async () => await Task.WhenAll(tasks);

        // Assert - No exceptions thrown
        await act.Should()
            .NotThrowAsync("losing advisory lock attempts should skip gracefully without error");
    }

    #region Helper Methods

    /// <summary>
    /// Runs a ManifestManager cycle in its own scope with advisory lock support.
    /// Mirrors what <see cref="ManifestManagerPollingService"/> does per cycle.
    /// </summary>
    private async Task RunManifestManagerInScope()
    {
        using var scope = Scope
            .ServiceProvider.GetRequiredService<IServiceProvider>()
            .CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

        if (dataContext is DbContext dbContext)
        {
            using var transaction = await dataContext.BeginTransaction();

            var acquired = await dbContext
                .Database.SqlQuery<bool>(
                    $"""SELECT pg_try_advisory_xact_lock(hashtext('trax_manifest_manager')) AS "Value" """
                )
                .FirstAsync();

            if (!acquired)
            {
                await dataContext.RollbackTransaction();
                return;
            }

            var train = scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
            await train.Run(Unit.Default);

            await dataContext.CommitTransaction();
        }
        else
        {
            var train = scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
            await train.Run(Unit.Default);
        }
    }

    #endregion
}
