using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Scheduling;
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
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("p\0" + principal)));

    [Test]
    public async Task A_one_off_manifest_started_by_a_caller_queues_its_run_for_them()
    {
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        var started = await scheduler.ScheduleOnceAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit
        >(
            "starter-once",
            new SchedulerTestInput { Value = "mine" },
            TimeSpan.Zero,
            o => o.StartedBy("apikey:alice")
        );
        var nobody = await scheduler.ScheduleOnceAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit
        >("starter-nobody", new SchedulerTestInput { Value = "theirs" }, TimeSpan.Zero);

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
        var manifest = await scheduler.ScheduleOnceAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit
        >(
            "starter-trigger",
            new SchedulerTestInput { Value = "again" },
            TimeSpan.FromDays(1),
            o => o.StartedBy("apikey:alice")
        );

        await scheduler.TriggerAsync("starter-trigger");

        (await StarterOfEntryFor(manifest.Id)).Should().Be(HashOf("apikey:alice"));
    }

    [Test]
    public async Task Reseeding_with_StartedBy_null_clears_the_starter()
    {
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        Task<Manifest> Seed(Action<ScheduleOptions>? options) =>
            scheduler.ScheduleAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "starter-reseed",
                new SchedulerTestInput { Value = "again" },
                Every.Minutes(5),
                options
            );

        var manifest = await Seed(o => o.StartedBy("apikey:alice"));
        await Seed(null);
        (await StarterOfManifest(manifest.Id))
            .Should()
            .Be(HashOf("apikey:alice"), "a re-seed that does not call StartedBy keeps the starter");

        await Seed(o => o.StartedBy(null));

        (await StarterOfManifest(manifest.Id))
            .Should()
            .BeNull(
                "StartedBy(null) records nobody, so the previous caller no longer follows its runs"
            );
    }

    private async Task<string?> StarterOfManifest(long manifestId)
    {
        DataContext.Reset();
        return await ((DbContext)DataContext)
            .Database.SqlQuery<string?>(
                $"SELECT starter_hash AS \"Value\" FROM trax.manifest WHERE id = {manifestId}"
            )
            .SingleAsync();
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
