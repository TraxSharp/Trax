using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A manifest scheduled <c>StartedBy</c> a caller queues every run of it for that caller, so the caller may follow
/// each run and retry; one scheduled without it queues runs nobody started.
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class ManifestStarterTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    private static string HashOf(string principal) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(principal)));

    [Test]
    public async Task A_one_off_manifest_started_by_a_caller_queues_its_run_for_them()
    {
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        var started = await scheduler.ScheduleOnceAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "starter-once",
            new SchedulerTestInput { Value = "mine" },
            TimeSpan.Zero,
            o => o.StartedBy("apikey:alice")
        );
        var nobody = await scheduler.ScheduleOnceAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "starter-nobody",
            new SchedulerTestInput { Value = "theirs" },
            TimeSpan.Zero
        );

        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);

        (await StarterOfEntryFor(started.Id))
            .Should()
            .Be(HashOf("apikey:alice"), "every run of the manifest is the caller's, per " + Adr);
        (await StarterOfEntryFor(nobody.Id)).Should().BeNull("nobody started it, per " + Adr);
    }

    [Test]
    public async Task A_trigger_of_the_manifest_is_queued_for_its_starter_too()
    {
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        var manifest = await scheduler.ScheduleOnceAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "starter-trigger",
            new SchedulerTestInput { Value = "again" },
            TimeSpan.FromDays(1),
            o => o.StartedBy("apikey:alice")
        );

        await scheduler.TriggerAsync("starter-trigger");

        (await StarterOfEntryFor(manifest.Id)).Should().Be(HashOf("apikey:alice"));
    }

    private async Task<string?> StarterOfEntryFor(long manifestId)
    {
        DataContext.Reset();
        return await ((DbContext)DataContext)
            .Database.SqlQuery<string?>(
                $"SELECT starter_hash AS \"Value\" FROM trax.work_queue WHERE manifest_id = {manifestId}"
            )
            .SingleAsync();
    }
}
