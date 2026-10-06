using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A manifest's train that gives up waiting on an upstream (an <see cref="HttpClient"/>
/// timeout, which surfaces as a <see cref="TaskCanceledException"/>) failed; nobody cancelled
/// it. Trax.Effect records such a run <see cref="TrainState.Failed"/> and classifies it
/// <see cref="FailureClass.Transient"/> (Trax.Docs/adr/0020), and the scheduler counts only
/// Failed runs toward a manifest's retries. These tests pin that end to end, through the
/// ManifestManager, the dispatcher and the job runner against Postgres.
/// </summary>
[TestFixture]
public class HttpTimeoutRetryTests
{
    [Test]
    public async Task A_run_whose_HttpClient_times_out_is_recorded_as_a_transient_failure()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleOnce<IHttpTimeoutSchedulerTestTrain>(
                "http-timeout",
                new HttpTimeoutSchedulerTestInput(),
                TimeSpan.Zero,
                o => o.MaxRetries(3)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();
        await fx.RunJobDispatcherAsync();

        var run = await fx
            .DataContext.Metadatas.AsNoTracking()
            .SingleAsync(m => m.Manifest!.ExternalId == "http-timeout");

        run.TrainState.Should()
            .Be(TrainState.Failed, "an HttpClient timeout is not a cancellation anyone asked for");
        run.FailureClass.Should().Be(FailureClass.Transient);
        run.CancellationRequested.Should().BeFalse();
    }

    [Test]
    public async Task A_manifest_retries_a_run_whose_HttpClient_timed_out_and_dead_letters_it_at_max_retries()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleOnce<IHttpTimeoutSchedulerTestTrain>(
                "http-timeout-retry",
                new HttpTimeoutSchedulerTestInput(),
                TimeSpan.Zero,
                o => o.MaxRetries(1)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        // First attempt times out.
        await fx.RunManifestManagerAsync();
        await fx.RunJobDispatcherAsync();

        // The failure is the first of two allowed attempts, so the manifest is queued again.
        await fx.RunManifestManagerAsync();
        var queuedAgain = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .CountAsync(w =>
                w.Manifest!.ExternalId == "http-timeout-retry" && w.Status == WorkQueueStatus.Queued
            );
        queuedAgain.Should().Be(1, "a timed-out run is a failure, and a failure is retried");

        // The retry is queued behind the retry delay; stand in for that time passing.
        await fx
            .DataContext.WorkQueues.Where(w =>
                w.Manifest!.ExternalId == "http-timeout-retry" && w.Status == WorkQueueStatus.Queued
            )
            .ExecuteUpdateAsync(u => u.SetProperty(w => w.ScheduledAt, (DateTime?)null));

        // The retry times out too, which exceeds MaxRetries: the next cycle dead-letters it.
        await fx.RunJobDispatcherAsync();
        await fx.RunManifestManagerAsync();

        var runs = await fx
            .DataContext.Metadatas.AsNoTracking()
            .Where(m => m.Manifest!.ExternalId == "http-timeout-retry")
            .ToListAsync();
        runs.Should().HaveCount(2);
        runs.Should()
            .OnlyContain(m =>
                m.TrainState == TrainState.Failed && m.FailureClass == FailureClass.Transient
            );

        var deadLetters = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .Where(d => d.Manifest!.ExternalId == "http-timeout-retry")
            .ToListAsync();
        deadLetters.Should().ContainSingle().Which.Reason.Should().Contain("Max retries exceeded");
    }
}
