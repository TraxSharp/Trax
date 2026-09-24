using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// How far the caller's cancellation token reaches on the queued execution path — the token a
/// GraphQL resolver supplies as <c>ctx.RequestAborted</c> when a mutation runs with
/// <c>mode: QUEUE</c>.
///
/// <para>It covers the enqueue and nothing beyond it. Once the work queue row is committed the
/// submission belongs to the scheduler, which runs it under its own token, so a caller that
/// disconnects or times out cannot recall the work. Cancelling a queued submission is a
/// separate, durable mechanism: the <c>cancel_requested</c> flag on the execution row.</para>
/// </summary>
[TestFixture]
public class QueueCancellationTests
{
    [Test]
    public async Task Queue_WhenTheCallerCancelsAfterEnqueueing_TheWorkStillRuns()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "queue-cancel-after",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(5)
            )
        );

        var execution = fx.Services.GetRequiredService<ITrainExecutionService>();
        using var cts = new CancellationTokenSource();

        var receipt = await execution.QueueAsync(
            typeof(ISchedulerTestTrain).FullName!,
            """{"value":"not-recallable"}""",
            ct: cts.Token
        );

        // The caller gives up — a client timeout, a closed connection, a cancelled request.
        await cts.CancelAsync();

        await fx.RunJobDispatcherAsync();
        fx.DataContext.Reset();

        var entry = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .FirstAsync(w => w.Id == receipt.WorkQueueId);

        entry
            .Status.Should()
            .Be(
                WorkQueueStatus.Dispatched,
                "the submission is durable once committed — the caller's token no longer governs it"
            );
        entry.MetadataId.Should().NotBeNull();

        // Dispatched only says the scheduler took the row. The in-memory submitter runs the train
        // inline before the dispatcher returns, so the run has to be terminal by now, and it has
        // to have finished rather than been cancelled by the token the caller abandoned.
        var run = await fx
            .DataContext.Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == entry.MetadataId);

        run.TrainState.Should()
            .Be(
                TrainState.Completed,
                "the run belongs to the scheduler once enqueued, so the caller's cancellation "
                    + "must not reach it"
            );
    }

    [Test]
    public async Task Queue_WithAnAlreadyCancelledToken_DoesNotEnqueueAnything()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "queue-cancel-before",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(5)
            )
        );

        var execution = fx.Services.GetRequiredService<ITrainExecutionService>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var queue = async () =>
            await execution.QueueAsync(
                typeof(ISchedulerTestTrain).FullName!,
                """{"value":"never-queued"}""",
                ct: cts.Token
            );

        await queue.Should().ThrowAsync<OperationCanceledException>();

        fx.DataContext.Reset();
        var entries = await fx.DataContext.WorkQueues.AsNoTracking().ToListAsync();
        entries.Should().BeEmpty("the token does govern the enqueue itself");
    }
}
