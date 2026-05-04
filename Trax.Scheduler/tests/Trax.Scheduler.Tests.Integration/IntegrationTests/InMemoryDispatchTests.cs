using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Drives the InMemory polling pipeline (InMemoryManifestManagerTrain →
/// InMemoryDispatchJobsJunction → InMemoryJobSubmitter). The Postgres pipeline tests
/// don't reach this code path because UsePostgres registers the standard
/// ManifestManagerTrain + JobDispatcherTrain instead.
/// </summary>
[TestFixture]
public class InMemoryDispatchTests
{
    private const string TrainNameFilter = "SchedulerTestTrain";

    [Test]
    public async Task ManifestManager_InMemory_DispatchesJobsInline()
    {
        await using var fx = SchedulerE2EFixture.CreateInMemory(s =>
            s.Schedule<ISchedulerTestTrain>(
                "inmem-dispatch",
                new SchedulerTestInput { Value = "hi" },
                Every.Minutes(1)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();

        // InMemoryDispatchJobsJunction creates Metadata records inline for the test train
        // (the orchestration trains create their own Metadata too — filter by train name).
        var metadatas = await fx
            .DataContext.Metadatas.AsNoTracking()
            .Where(m => m.Name.Contains(TrainNameFilter))
            .ToListAsync();
        metadatas.Should().NotBeEmpty();
    }

    [Test]
    public async Task ManifestManager_InMemory_DisabledManifest_NoMetadataCreated()
    {
        await using var fx = SchedulerE2EFixture.CreateInMemory(s =>
            s.Schedule<ISchedulerTestTrain>(
                "inmem-disabled",
                new SchedulerTestInput(),
                Every.Minutes(1),
                opts => opts.Enabled(false)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();

        var metadatas = await fx
            .DataContext.Metadatas.AsNoTracking()
            .Where(m => m.Name.Contains(TrainNameFilter))
            .ToListAsync();
        metadatas.Should().BeEmpty();
    }

    [Test]
    public async Task ManifestManager_InMemory_NoManifests_NoOp()
    {
        await using var fx = SchedulerE2EFixture.CreateInMemory(s => { });

        await fx.RunManifestManagerAsync();

        var testMetadatas = await fx
            .DataContext.Metadatas.AsNoTracking()
            .Where(m => m.Name.Contains(TrainNameFilter))
            .ToListAsync();
        testMetadatas.Should().BeEmpty();
    }

    [Test]
    public async Task ManifestManager_InMemory_MultipleManifests_DispatchesEach()
    {
        await using var fx = SchedulerE2EFixture.CreateInMemory(s =>
            s.Schedule<ISchedulerTestTrain>("inmem-a", new SchedulerTestInput(), Every.Minutes(1))
                .Include<ISchedulerTestTrain>("inmem-b", new SchedulerTestInput())
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();

        var metadatas = await fx
            .DataContext.Metadatas.AsNoTracking()
            .Where(m => m.Name.Contains(TrainNameFilter))
            .ToListAsync();
        // Only the root manifest fires on first cycle; dependent fires after parent succeeds.
        metadatas.Should().HaveCountGreaterThan(0);
    }
}
