using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>ScheduleOptions.Priority</c> is "the dispatch priority for this manifest". Two manifests in
/// one group with different priorities must keep them.
/// </summary>
[TestFixture]
public class SharedGroupManifestPriorityTests
{
    [Test]
    public async Task Each_manifest_in_a_shared_group_is_queued_at_its_own_priority()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        var urgent = await fx.Scheduler.ScheduleAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit
        >(
            "shared-urgent",
            new SchedulerTestInput { Value = "urgent" },
            Every.Minutes(5),
            options => options.Group("shared").Priority(30)
        );
        var routine = await fx.Scheduler.ScheduleAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit
        >(
            "shared-routine",
            new SchedulerTestInput { Value = "routine" },
            Every.Minutes(5),
            options => options.Group("shared").Priority(1)
        );

        await fx.RunManifestManagerAsync();

        var entries = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .Where(q => q.Status == WorkQueueStatus.Queued)
            .ToDictionaryAsync(q => q.ManifestId!.Value, q => q.Priority);

        entries[urgent.Id].Should().Be(30, "the urgent manifest was scheduled with Priority(30)");
        entries[routine.Id].Should().Be(1, "the routine manifest was scheduled with Priority(1)");
    }

    [Test]
    public async Task A_manifest_joining_a_group_without_group_options_keeps_the_group_limit_another_manifest_set()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await fx.Scheduler.ScheduleAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "limited-a",
            new SchedulerTestInput { Value = "a" },
            Every.Minutes(5),
            options => options.Group("limited", group => group.MaxActiveJobs(2))
        );
        await fx.Scheduler.ScheduleAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "limited-b",
            new SchedulerTestInput { Value = "b" },
            Every.Minutes(5),
            options => options.Group("limited")
        );

        var group = await fx
            .DataContext.ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Name == "limited");

        group
            .MaxActiveJobs.Should()
            .Be(2, "the second manifest joined the group and said nothing about its limit");
    }
}
