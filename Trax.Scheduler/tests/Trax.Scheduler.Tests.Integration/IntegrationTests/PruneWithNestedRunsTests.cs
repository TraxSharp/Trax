using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A scheduled train that runs another train records the inner run as a child of its own
/// (<c>metadata.parent_id</c>). Pruning the manifest deletes its runs, and the child's reference
/// must not stop that.
/// </summary>
[TestFixture]
public class PruneWithNestedRunsTests : TestSetup
{
    private const string Owner = "prune-with-nested-runs-tests";

    [Test]
    public async Task Startup_prune_removes_a_manifest_whose_run_started_a_nested_train()
    {
        var orphan = await CreateManifest("removed-from-code");
        var parentRun = await CreateMetadata(orphan.Id, parentId: null);
        var childRun = await CreateMetadata(manifestId: null, parentId: parentRun.Id);
        await CreateManifest("still-configured");

        var configuration = new SchedulerConfiguration
        {
            PruneOrphanedManifests = true,
            RecoverStuckJobsOnStartup = false,
            HasDatabaseProvider = true,
            Owner = Owner,
        };
        configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = "still-configured",
                ExpectedExternalIds = ["still-configured"],
                ScheduleFunc = (_, _) => Task.FromResult<Manifest>(null!),
            }
        );
        var startup = new SchedulerStartupService(
            Scope.ServiceProvider,
            configuration,
            Scope
                .ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger<SchedulerStartupService>()
        );

        var start = () => startup.StartAsync(CancellationToken.None);

        await start
            .Should()
            .NotThrowAsync("a host must be able to start after a schedule is removed");

        DataContext.Reset();
        (await DataContext.Manifests.AnyAsync(m => m.ExternalId == "removed-from-code"))
            .Should()
            .BeFalse();
        await AssertChildRunKeptWithoutParent(childRun.Id);
    }

    [Test]
    public async Task ScheduleMany_prefix_prune_removes_a_manifest_whose_run_started_a_nested_train()
    {
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        var stale = await CreateManifest("batch-stale");
        var parentRun = await CreateMetadata(stale.Id, parentId: null);
        var childRun = await CreateMetadata(manifestId: null, parentId: parentRun.Id);

        await scheduler.ScheduleManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
            ["kept"],
            id => ($"batch-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("batch-")
        );

        DataContext.Reset();
        (await DataContext.Manifests.AnyAsync(m => m.ExternalId == "batch-stale"))
            .Should()
            .BeFalse("the prefix prune removes manifests no longer in the batch");
        await AssertChildRunKeptWithoutParent(childRun.Id);
    }

    [Test]
    public async Task A_startup_prune_that_fails_is_logged_and_the_host_still_starts()
    {
        var undeletable = await CreateManifest("prune-fails");
        await CreateMetadata(undeletable.Id, parentId: null);
        await CreateManifest("still-configured-2");

        var db = (DbContext)DataContext;
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION trax.test_refuse_manifest_delete() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'refused by test';
            END $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS test_refuse_manifest_delete ON trax.manifest;
            CREATE TRIGGER test_refuse_manifest_delete BEFORE DELETE ON trax.manifest
                FOR EACH ROW WHEN (OLD.external_id = 'prune-fails')
                EXECUTE FUNCTION trax.test_refuse_manifest_delete();
            """
        );

        try
        {
            var startup = CreateStartupService("still-configured-2");

            await startup
                .Invoking(s => s.StartAsync(CancellationToken.None))
                .Should()
                .NotThrowAsync("pruning is housekeeping and must not stop a host");

            DataContext.Reset();
            (await DataContext.Manifests.AnyAsync(m => m.Id == undeletable.Id)).Should().BeTrue();
            (await DataContext.Metadatas.AnyAsync(m => m.ManifestId == undeletable.Id))
                .Should()
                .BeTrue("the failed batch rolled back, so its runs were not deleted either");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS test_refuse_manifest_delete ON trax.manifest;"
            );
        }
    }

    private async Task AssertChildRunKeptWithoutParent(long childRunId)
    {
        var child = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == childRunId);
        child.Should().NotBeNull("a nested run outlives the run that started it");
        child!.ParentId.Should().BeNull();
    }

    private SchedulerStartupService CreateStartupService(string configuredExternalId)
    {
        var configuration = new SchedulerConfiguration
        {
            PruneOrphanedManifests = true,
            RecoverStuckJobsOnStartup = false,
            HasDatabaseProvider = true,
            Owner = Owner,
        };
        configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = configuredExternalId,
                ExpectedExternalIds = [configuredExternalId],
                ScheduleFunc = (_, _) => Task.FromResult<Manifest>(null!),
            }
        );
        return new SchedulerStartupService(
            Scope.ServiceProvider,
            configuration,
            Scope
                .ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger<SchedulerStartupService>()
        );
    }

    private async Task<Manifest> CreateManifest(string externalId)
    {
        var group = await CreateAndSaveManifestGroup(
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
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = externalId },
            }
        );
        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = group.Id;
        manifest.Owner = Owner;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task<Metadata> CreateMetadata(long? manifestId, long? parentId)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "run" },
                ManifestId = manifestId,
                ParentId = parentId,
            }
        );
        metadata.TrainState = TrainState.Completed;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return metadata;
    }
}
