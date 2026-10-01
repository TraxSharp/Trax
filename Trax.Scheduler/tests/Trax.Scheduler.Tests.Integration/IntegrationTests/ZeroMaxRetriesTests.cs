using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>MaxRetries(0)</c> reads as "run it, and do not retry a failure". A manifest that has never
/// failed has nothing to dead-letter.
/// </summary>
[TestFixture]
public class ZeroMaxRetriesTests : TestSetup
{
    [Test]
    public async Task A_manifest_with_zero_max_retries_is_queued_rather_than_dead_lettered_before_it_ever_runs()
    {
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        var manifest = await scheduler.ScheduleAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "no-retries",
            new SchedulerTestInput { Value = "x" },
            Every.Minutes(5),
            options => options.MaxRetries(0)
        );

        var train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        await train.Run(Unit.Default);

        DataContext.Reset();
        var deadLetters = await DataContext
            .DeadLetters.AsNoTracking()
            .Where(d => d.ManifestId == manifest.Id)
            .ToListAsync();
        var queued = await DataContext
            .WorkQueues.AsNoTracking()
            .CountAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);

        deadLetters
            .Should()
            .BeEmpty("the manifest has never run, so it has no failure to dead-letter");
        queued.Should().Be(1, "a new interval manifest is due on the first cycle");
    }
}
