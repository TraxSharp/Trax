using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Emission tests that exercise the Postgres pipeline (the real
/// <c>CreateWorkQueueEntriesJunction</c> and <c>DispatchJobsJunction</c>, plus the batch requeue and
/// trigger-group paths), which the InMemory pipeline replaces with an inline dispatcher and so
/// cannot cover. Requires Postgres, like the rest of this integration suite.
/// </summary>
[TestFixture]
public class ChangeSignalEmissionPostgresTests
{
    private static async Task<SchedulerE2EFixture> CreateAsync(
        RecordingChangeSignal recording,
        Action<Trax.Scheduler.Configuration.SchedulerConfigurationBuilder> configureScheduler
    ) =>
        await SchedulerE2EFixture.CreateAsync(
            configureScheduler,
            services => services.AddSingleton<ITraxChangeSignal>(recording)
        );

    private static async Task<DeadLetter> SeedDeadLetterAsync(
        SchedulerE2EFixture fx,
        string externalId
    )
    {
        var manifest = await fx
            .DataContext.Manifests.Include(m => m.ManifestGroup)
            .FirstAsync(m => m.ExternalId == externalId);
        var dl = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "seeded",
                RetryCount = 3,
            }
        );
        await fx.DataContext.Track(dl);
        await fx.DataContext.SaveChanges(default);
        fx.DataContext.Reset();
        return dl;
    }

    [Test]
    public async Task ManifestManagerCycle_CreatesWorkQueueEntries_EmitsWorkQueue()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateAsync(
            recording,
            s =>
                s.Schedule<ISchedulerTestTrain>(
                    "pg-create",
                    new SchedulerTestInput(),
                    Every.Minutes(1)
                )
        );
        await fx.MaterializePendingManifestsAsync();
        recording.Clear();

        await fx.RunManifestManagerAsync();

        recording
            .Domains.Should()
            .Contain(ChangeDomain.WorkQueue, "CreateWorkQueueEntriesJunction wrote entries");
        recording
            .Domains.Should()
            .NotContain(ChangeDomain.Manifest, "a routine cycle must not signal a manifest change");
    }

    [Test]
    public async Task JobDispatcher_DispatchesEntries_EmitsWorkQueue()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateAsync(
            recording,
            s =>
                s.Schedule<ISchedulerTestTrain>(
                    "pg-dispatch",
                    new SchedulerTestInput(),
                    Every.Minutes(1)
                )
        );
        await fx.MaterializePendingManifestsAsync();
        await fx.RunManifestManagerAsync(); // create the queued entries
        recording.Clear();

        await fx.RunJobDispatcherAsync();

        recording
            .Domains.Should()
            .Contain(ChangeDomain.WorkQueue, "DispatchJobsJunction moved entries to dispatched");
    }

    [Test]
    public async Task RequeueAllDeadLetters_EmitsDeadLetterAndWorkQueue()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateAsync(
            recording,
            s =>
                s.Schedule<ISchedulerTestTrain>(
                    "pg-req",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
        );
        await fx.MaterializePendingManifestsAsync();
        await SeedDeadLetterAsync(fx, "pg-req");
        recording.Clear();

        var result = await fx.Scheduler.RequeueAllDeadLettersAsync();

        result.Count.Should().BeGreaterThan(0);
        recording
            .Domains.Should()
            .BeEquivalentTo(new[] { ChangeDomain.DeadLetter, ChangeDomain.WorkQueue });
    }

    [Test]
    public async Task TriggerGroup_EmitsWorkQueue()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateAsync(
            recording,
            s =>
                s.Schedule<ISchedulerTestTrain>(
                    "pg-grp",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
        );
        await fx.MaterializePendingManifestsAsync();
        var groupId = await fx.DataContext.ManifestGroups.Select(g => g.Id).FirstAsync();
        recording.Clear();

        var count = await fx.Scheduler.TriggerGroupAsync(groupId);

        count.Should().BeGreaterThan(0);
        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.WorkQueue);
    }
}
