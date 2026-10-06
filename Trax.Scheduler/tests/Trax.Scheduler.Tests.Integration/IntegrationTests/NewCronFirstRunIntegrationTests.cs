using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A daily cron scheduled at 14:00 for 03:00 runs at 03:00, not at the next poll.
/// </summary>
[TestFixture]
public class NewCronFirstRunIntegrationTests
{
    [Test]
    public async Task A_new_daily_cron_is_not_queued_before_its_first_occurrence()
    {
        // Two hours ahead, so the first occurrence cannot fall inside the test.
        var at = DateTime.UtcNow.AddHours(2);
        var before = DateTime.UtcNow;

        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "daily-later",
                new SchedulerTestInput { Value = "x" },
                Cron.Daily(hour: at.Hour, minute: at.Minute)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .SingleAsync(m => m.ExternalId == "daily-later");
        (await fx.DataContext.WorkQueues.AsNoTracking().AnyAsync(q => q.ManifestId == manifest.Id))
            .Should()
            .BeFalse("the first occurrence is two hours away");

        var expected = CronParser.Parse(manifest.CronExpression!).GetNextOccurrence(before)!.Value;
        manifest.NextScheduledRun.Should().Be(expected);
    }
}
