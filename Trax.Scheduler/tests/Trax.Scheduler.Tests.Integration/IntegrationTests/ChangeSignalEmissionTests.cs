using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Verifies that scheduler write paths emit the correct <see cref="ChangeDomain"/> change signal
/// (the push behind the dashboard's <c>onDataChanged</c> subscription). Uses the InMemory pipeline
/// with a recording <see cref="ITraxChangeSignal"/> so the assertions run without Postgres. The
/// Postgres-only work-queue junctions (CreateWorkQueueEntries, DispatchJobs) are covered by the
/// same guarded-Notify pattern proven here and by the Postgres pipeline suites.
/// </summary>
[TestFixture]
public class ChangeSignalEmissionTests
{
    private static SchedulerE2EFixture CreateFixture(
        RecordingChangeSignal recording,
        Action<Trax.Scheduler.Configuration.SchedulerConfigurationBuilder>? configureScheduler =
            null
    ) =>
        SchedulerE2EFixture.CreateInMemory(
            configureScheduler ?? (_ => { }),
            services => services.AddSingleton<ITraxChangeSignal>(recording)
        );

    private static async Task<SchedulerE2EFixture> CreateWithManifestAsync(
        RecordingChangeSignal recording,
        string externalId
    )
    {
        var fx = CreateFixture(
            recording,
            s =>
                s.Schedule<ISchedulerTestTrain>(
                    externalId,
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
        );
        await fx.MaterializePendingManifestsAsync();
        recording.Clear();
        return fx;
    }

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

    private static string TrainName => typeof(ISchedulerTestTrain).FullName!;

    #region OperationsService

    [Test]
    public async Task QueueTrain_EmitsWorkQueue()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = CreateFixture(recording);
        var ops = fx.Services.GetRequiredService<IOperationsService>();

        await ops.QueueTrainAsync(new QueueTrainInput(TrainName), CancellationToken.None);

        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.WorkQueue);
    }

    [Test]
    public async Task CancelWorkQueueEntry_EmitsWorkQueue()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = CreateFixture(recording);
        var ops = fx.Services.GetRequiredService<IOperationsService>();

        var queued = await ops.QueueTrainAsync(
            new QueueTrainInput(TrainName),
            CancellationToken.None
        );
        recording.Clear();

        await ops.CancelWorkQueueEntryAsync(queued.Id!.Value, CancellationToken.None);

        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.WorkQueue);
    }

    [Test]
    public async Task UpdateSchedulerConfig_WhenChanged_EmitsSchedulerConfig()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = CreateFixture(recording);
        var ops = fx.Services.GetRequiredService<IOperationsService>();

        await ops.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(DefaultMaxRetries: 8),
            CancellationToken.None
        );

        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.SchedulerConfig);
    }

    [Test]
    public async Task UpdateSchedulerConfig_NoChange_EmitsNothing()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = CreateFixture(recording);
        var ops = fx.Services.GetRequiredService<IOperationsService>();

        // Apply a change, clear, then re-apply the same value: the second call is a no-op write.
        await ops.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(DefaultMaxRetries: 8),
            CancellationToken.None
        );
        recording.Clear();

        await ops.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(DefaultMaxRetries: 8),
            CancellationToken.None
        );

        recording.Domains.Should().BeEmpty("a no-op config patch must not signal a change");
    }

    [Test]
    public async Task UpdateManifestGroup_WhenChanged_EmitsManifestGroup()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateWithManifestAsync(recording, "grp-1");
        var ops = fx.Services.GetRequiredService<IOperationsService>();

        var groupId = await fx.DataContext.ManifestGroups.Select(g => g.Id).FirstAsync();

        await ops.UpdateManifestGroupAsync(
            groupId,
            new UpdateManifestGroupInput(MaxActiveJobs: 5),
            CancellationToken.None
        );

        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.ManifestGroup);
    }

    #endregion

    #region TraxScheduler

    [Test]
    public async Task Enable_EmitsManifest()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateWithManifestAsync(recording, "en-1");

        await fx.Scheduler.EnableAsync("en-1");

        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.Manifest);
    }

    [Test]
    public async Task Disable_EmitsManifest()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateWithManifestAsync(recording, "dis-1");

        await fx.Scheduler.DisableAsync("dis-1");

        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.Manifest);
    }

    [Test]
    public async Task Trigger_EmitsWorkQueue()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateWithManifestAsync(recording, "trig-1");

        await fx.Scheduler.TriggerAsync("trig-1");

        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.WorkQueue);
    }

    [Test]
    public async Task AcknowledgeDeadLetter_EmitsDeadLetter()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateWithManifestAsync(recording, "ack-1");
        var dl = await SeedDeadLetterAsync(fx, "ack-1");
        recording.Clear();

        await fx.Scheduler.AcknowledgeDeadLetterAsync(dl.Id, "handled");

        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.DeadLetter);
    }

    [Test]
    public async Task RequeueDeadLetter_EmitsDeadLetterAndWorkQueue()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateWithManifestAsync(recording, "req-1");
        var dl = await SeedDeadLetterAsync(fx, "req-1");
        recording.Clear();

        await fx.Scheduler.RequeueDeadLetterAsync(dl.Id);

        recording
            .Domains.Should()
            .BeEquivalentTo(new[] { ChangeDomain.DeadLetter, ChangeDomain.WorkQueue });
    }

    [Test]
    public async Task AcknowledgeAllDeadLetters_EmitsDeadLetter()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await CreateWithManifestAsync(recording, "ackall-1");
        await SeedDeadLetterAsync(fx, "ackall-1");
        recording.Clear();

        var result = await fx.Scheduler.AcknowledgeAllDeadLettersAsync("bulk");

        result.Count.Should().BeGreaterThan(0);
        recording.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.DeadLetter);
    }

    [Test]
    public async Task AcknowledgeAllDeadLetters_WhenNone_EmitsNothing()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = CreateFixture(recording);

        var result = await fx.Scheduler.AcknowledgeAllDeadLettersAsync("bulk");

        result.Count.Should().Be(0);
        recording.Domains.Should().BeEmpty("no dead letters were resolved, so nothing changed");
    }

    #endregion

    #region Negative: routine cycle does not spam Manifest

    [Test]
    public async Task ManifestManagerCycle_HealthyManifest_DoesNotEmitManifest()
    {
        // A routine manager cycle recomputes NextRunTime and enqueues work; it must not emit the
        // Manifest domain (which is reserved for user-facing manifest edits/enable/disable). If it
        // did, every poll would trigger a manifests-grid refetch.
        var recording = new RecordingChangeSignal();
        await using var fx = CreateFixture(
            recording,
            s =>
                s.Schedule<ISchedulerTestTrain>(
                    "cycle-1",
                    new SchedulerTestInput(),
                    Every.Minutes(1)
                )
        );
        await fx.MaterializePendingManifestsAsync();
        recording.Clear();

        await fx.RunManifestManagerAsync();

        recording.Domains.Should().NotContain(ChangeDomain.Manifest);
    }

    #endregion
}
