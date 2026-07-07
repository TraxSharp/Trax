using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Tests.Stress.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Tests.Stress.IntegrationTests;

/// <summary>
/// Scale tests for the resilience-hardening changes: metadata cleanup at incident volume with
/// FK-referenced rows, the per-manifest FailedCount subquery over a large terminal history, and
/// the liveness monitor under concurrent access.
///
/// Run with: dotnet test --filter "TestCategory=Stress"
/// NOT run on every PR.
/// </summary>
[TestFixture]
public class ResilienceStressTests : TestSetup
{
    #region Metadata Cleanup at Incident Scale

    [Test]
    public async Task MetadataCleanup_BloatedTableWithFkReferences_BoundsTableAndKeepsDeadLetters()
    {
        // Reproduces the incident shape at scale. The table is bloated with JobDispatcher
        // metadata (never whitelisted, so it grew unbounded), and some rows are FK-referenced
        // by dead letters (retry_metadata_id) and by child metadata (parent_id) -- the RESTRICT
        // foreign keys that made the old cleanup DELETE abort the whole sweep. The real cleanup
        // train must prune all of it, clear the back-references, and keep the dead letters.
        const int dispatcherRows = 40_000;
        const int deadLetterRefRows = 2_000;
        const int parentChildPairs = 1_000;

        var group = await SeedManifestGroup("cleanup-scale");
        var manifest = (await SeedManifests(1, group.Id))[0];

        var dispatcherName = typeof(JobDispatcherTrain).FullName!;
        var expired = DateTime.UtcNow.AddHours(-2);

        await SeedAdminMetadata(dispatcherName, dispatcherRows, expired);
        var referencedIds = await SeedAdminMetadataReturningIds(
            dispatcherName,
            deadLetterRefRows,
            expired
        );
        await SeedDeadLettersReferencing(manifest, referencedIds);
        await SeedExpiredParentsWithRunningChildren(dispatcherName, parentChildPairs, expired);

        var before = await DataContext.Metadatas.CountAsync();
        before
            .Should()
            .Be(dispatcherRows + deadLetterRefRows + (parentChildPairs * 2), "seeded row count");
        TestContext.Out.WriteLine($"Seeded {before} metadata rows");

        // The stress fixture doesn't call AddMetadataCleanup, so enable it on the shared config.
        var config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        config.MetadataCleanup = new MetadataCleanupConfiguration
        {
            RetentionPeriod = TimeSpan.FromMinutes(30),
            DeleteBatchSize = 1000,
        };
        var train = Scope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();

        var elapsed = await AssertCompletesWithin(
            async () => await train.Run(new MetadataCleanupRequest()),
            TimeSpan.FromSeconds(60)
        );

        DataContext.Reset();

        // Every expired JobDispatcher row is pruned, bounding the table.
        var remainingDispatcher = await DataContext.Metadatas.CountAsync(m =>
            m.Name == dispatcherName
        );
        remainingDispatcher.Should().Be(0, "all expired JobDispatcher metadata should be pruned");

        // The still-running children survive (in-progress rows are never eligible). Note the
        // cleanup train also persists its own fresh MetadataCleanupTrain row, which is not past
        // retention yet, so a raw total would be parentChildPairs + 1.
        var survivingChildren = await DataContext.Metadatas.CountAsync(m =>
            m.Name == "Consumer.Trains.ChildTrain"
        );
        survivingChildren.Should().Be(parentChildPairs, "the in-progress children should survive");

        // Dead letters are kept, with their dangling retry reference nulled.
        var deadLetters = await DataContext.DeadLetters.CountAsync();
        deadLetters
            .Should()
            .Be(deadLetterRefRows, "dead letters are meaningful records and survive");
        var danglingDeadLetterRefs = await DataContext.DeadLetters.CountAsync(d =>
            d.RetryMetadataId != null
        );
        danglingDeadLetterRefs
            .Should()
            .Be(0, "no dead letter should reference a deleted metadata row");

        // Surviving children have their parent reference nulled, not left dangling.
        var childrenWithParent = await DataContext.Metadatas.CountAsync(m => m.ParentId != null);
        childrenWithParent.Should().Be(0, "child parent references should be nulled");

        TestContext.Out.WriteLine(
            $"Cleanup of {before} rows ({dispatcherRows} dispatcher, {deadLetterRefRows} dead-letter-referenced, "
                + $"{parentChildPairs} parent/child): {elapsed.TotalMilliseconds:F0}ms"
        );
    }

    #endregion

    #region FailedCount Over Large Terminal History

    [Test]
    public async Task FailedCountByManifest_WithLargeTerminalHistory_StaysBounded()
    {
        // LoadManifestsJunction computes a FailedCount per manifest on every dispatch cycle.
        // ix_metadata_manifest_failed must bound this to the (rare) failed rows instead of
        // scanning the manifest's entire terminal history. Without the partial index this
        // degrades as completed history accumulates.
        var group = await SeedManifestGroup("failed-count-scale");
        var manifest = (await SeedManifests(1, group.Id))[0];

        await SeedManifestMetadata(manifest, 50_000, TrainState.Completed);
        await SeedManifestMetadata(manifest, 500, TrainState.Failed);

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var failedCount = await DataContext
                    .Manifests.Where(m => m.Id == manifest.Id)
                    .Select(m => m.Metadatas.Count(md => md.TrainState == TrainState.Failed))
                    .FirstAsync();

                failedCount.Should().Be(500);
            },
            TimeSpan.FromSeconds(5)
        );

        TestContext.Out.WriteLine(
            $"FailedCount over a 50.5k-row manifest history: {elapsed.TotalMilliseconds:F0}ms"
        );
    }

    #endregion

    #region Liveness Monitor Concurrency

    [Test]
    public async Task LivenessMonitor_ConcurrentStampsAndReads_NeverThrowsOrTears()
    {
        // The monitor stores the last-dispatch timestamp as a 64-bit tick value updated with
        // Interlocked. A torn read would produce a garbage tick count that throws when wrapped
        // in DateTimeOffset. Hammer it from many threads to prove the read/write is atomic.
        var monitor = new SchedulerLivenessMonitor(TimeProvider.System);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var writers = Enumerable
            .Range(0, 8)
            .Select(_ =>
                Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                        monitor.RecordDispatchCycle();
                })
            );

        var readers = Enumerable
            .Range(0, 8)
            .Select(_ =>
                Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        var last = monitor.LastDispatchCompletedAt;
                        if (last is not null)
                            last.Value.Should().BeOnOrAfter(monitor.StartedAt);
                    }
                })
            );

        var run = async () => await Task.WhenAll(writers.Concat(readers));

        await run.Should().NotThrowAsync("Interlocked reads/writes must never tear");
        monitor.LastDispatchCompletedAt.Should().NotBeNull("writers stamped the monitor");
    }

    #endregion

    #region Seeding Helpers

    private async Task SeedAdminMetadata(string name, int count, DateTime startTime)
    {
        const int flushEvery = 5000;
        for (var i = 0; i < count; i++)
        {
            await DataContext.Track(NewTerminalAdminMetadata(name, startTime));
            if ((i + 1) % flushEvery == 0)
            {
                await DataContext.SaveChanges(CancellationToken.None);
                DataContext.Reset();
            }
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private async Task<List<long>> SeedAdminMetadataReturningIds(
        string name,
        int count,
        DateTime startTime
    )
    {
        var entities = new List<Metadata>(count);
        for (var i = 0; i < count; i++)
        {
            var metadata = NewTerminalAdminMetadata(name, startTime);
            await DataContext.Track(metadata);
            entities.Add(metadata);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        var ids = entities.Select(e => e.Id).ToList();
        DataContext.Reset();
        return ids;
    }

    private async Task SeedDeadLettersReferencing(Manifest manifest, List<long> metadataIds)
    {
        var reloaded = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);
        foreach (var id in metadataIds)
        {
            var deadLetter = DeadLetter.Create(
                new CreateDeadLetter
                {
                    Manifest = reloaded,
                    Reason = "scale-test",
                    RetryCount = 3,
                }
            );
            deadLetter.RetryMetadataId = id;
            await DataContext.Track(deadLetter);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private async Task SeedExpiredParentsWithRunningChildren(
        string adminName,
        int count,
        DateTime startTime
    )
    {
        var parents = new List<Metadata>(count);
        for (var i = 0; i < count; i++)
        {
            var parent = NewTerminalAdminMetadata(adminName, startTime);
            await DataContext.Track(parent);
            parents.Add(parent);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        var parentIds = parents.Select(p => p.Id).ToList();
        DataContext.Reset();

        foreach (var parentId in parentIds)
        {
            var child = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Consumer.Trains.ChildTrain",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                    ParentId = parentId,
                }
            );
            child.TrainState = TrainState.InProgress;
            child.StartTime = DateTime.UtcNow;
            await DataContext.Track(child);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private async Task SeedManifestMetadata(Manifest manifest, int count, TrainState state)
    {
        const int flushEvery = 5000;
        var baseTime = DateTime.UtcNow.AddHours(-1);
        for (var i = 0; i < count; i++)
        {
            var metadata = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Consumer.Trains.ICacheTrain",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                    ManifestId = manifest.Id,
                }
            );
            metadata.TrainState = state;
            metadata.StartTime = baseTime;
            metadata.EndTime = baseTime.AddSeconds(1);
            await DataContext.Track(metadata);
            if ((i + 1) % flushEvery == 0)
            {
                await DataContext.SaveChanges(CancellationToken.None);
                DataContext.Reset();
            }
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private static Metadata NewTerminalAdminMetadata(string name, DateTime startTime)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        metadata.TrainState = TrainState.Completed;
        metadata.StartTime = startTime;
        metadata.EndTime = startTime.AddSeconds(1);
        return metadata;
    }

    #endregion
}
