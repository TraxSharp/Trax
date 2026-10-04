using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>DefaultMaxRetries</c> and <c>DefaultMisfirePolicy</c> are the values a manifest gets when
/// its own options set neither.
/// </summary>
[TestFixture]
public class SeedingDefaultsTests
{
    [Test]
    public async Task DefaultMaxRetries_and_DefaultMisfirePolicy_apply_to_a_manifest_that_sets_neither()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.DefaultMaxRetries(7)
                .DefaultMisfirePolicy(MisfirePolicy.DoNothing)
                .Schedule<ISchedulerTestTrain>(
                    "uses-defaults",
                    new SchedulerTestInput { Value = "x" },
                    Every.Minutes(5)
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "uses-defaults");

        manifest.MaxRetries.Should().Be(7, "DefaultMaxRetries(7) is the scheduler-wide default");
        manifest
            .MisfirePolicy.Should()
            .Be(MisfirePolicy.DoNothing, "DefaultMisfirePolicy(DoNothing) is the default");
    }

    [Test]
    public async Task A_manifest_that_states_its_own_values_keeps_them_over_the_defaults()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.DefaultMaxRetries(7)
                .DefaultMisfirePolicy(MisfirePolicy.DoNothing)
                .Schedule<ISchedulerTestTrain>(
                    "states-own",
                    new SchedulerTestInput { Value = "x" },
                    Every.Minutes(5),
                    o => o.MaxRetries(2).OnMisfire(MisfirePolicy.FireOnceNow)
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "states-own");

        manifest.MaxRetries.Should().Be(2);
        manifest.MisfirePolicy.Should().Be(MisfirePolicy.FireOnceNow);
    }

    [Test]
    public async Task ScheduleMany_items_take_the_defaults_unless_configureEach_sets_a_value()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s => s.DefaultMaxRetries(6));

        await fx.Scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Trax.Core.Functional.Unit,
            string
        >(
            ["plain", "own"],
            id => ($"defaults-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            configureEach: (id, item) =>
            {
                if (id == "own")
                    item.MaxRetries = 1;
            }
        );

        var retries = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("defaults-"))
            .ToDictionaryAsync(m => m.ExternalId, m => m.MaxRetries);

        retries["defaults-plain"].Should().Be(6);
        retries["defaults-own"].Should().Be(1);
    }
}
