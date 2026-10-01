using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A named batch (<c>ScheduleMany("name", ...)</c>) prunes the manifests it no longer declares.
/// It must not prune another batch's manifests because that batch's name starts with its own.
/// </summary>
[TestFixture]
public class NamedBatchPrunePrefixTests
{
    [Test]
    public async Task A_named_batch_does_not_prune_the_manifests_of_a_batch_whose_name_extends_its_own()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                    "sync-users",
                    ["alice", "bob"],
                    id => (id, new SchedulerTestInput { Value = id }),
                    Every.Minutes(5)
                )
                .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                    "sync",
                    ["orders"],
                    id => (id, new SchedulerTestInput { Value = id }),
                    Every.Minutes(5)
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var externalIds = await fx
            .DataContext.Manifests.AsNoTracking()
            .Select(m => m.ExternalId)
            .ToListAsync();

        externalIds
            .Should()
            .Contain(
                ["sync-users-alice", "sync-users-bob", "sync-orders"],
                "both batches are declared; the 'sync' batch owns only its own manifests"
            );
    }
}
