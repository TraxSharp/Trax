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
/// Pins the correlation chain that makes a queued submission reconcilable: the receipt a
/// caller gets back from <c>ITrainExecutionService.QueueAsync</c> — the path behind a GraphQL
/// mutation run with <c>mode: QUEUE</c> — names the execution the scheduler later records.
///
/// <para>Two links carry it, both written by <c>DispatchJobsJunction</c>: the work queue
/// entry's <c>ExternalId</c> is reused as the execution's, and the entry is stamped with the
/// execution's id. Break either and a queued write becomes unattributable after the fact.</para>
/// </summary>
[TestFixture]
public class QueueCorrelationTests
{
    [Test]
    public async Task Dispatch_GivesTheExecution_TheExternalIdTheCallerWasHandedAtQueueTime()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "queue-correlation",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(5)
            )
        );

        var execution = fx.Services.GetRequiredService<ITrainExecutionService>();

        var receipt = await execution.QueueAsync(
            typeof(ISchedulerTestTrain).FullName!,
            """{"value":"reconcile-me"}""",
            ct: default
        );

        await fx.RunJobDispatcherAsync();
        fx.DataContext.Reset();

        var entry = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .FirstAsync(w => w.Id == receipt.WorkQueueId);

        entry.Status.Should().Be(WorkQueueStatus.Dispatched);
        entry
            .MetadataId.Should()
            .NotBeNull("dispatch stamps the entry with the execution it created");

        var metadata = await fx
            .DataContext.Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == entry.MetadataId);

        metadata
            .ExternalId.Should()
            .Be(
                receipt.ExternalId,
                "the queue receipt's externalId is the execution's externalId — a caller holding "
                    + "only the receipt can still say what happened to its submission"
            );
        metadata.Name.Should().Be(typeof(ISchedulerTestTrain).FullName);
    }

    [Test]
    public async Task QueuedSubmission_IsReachableFromTheReceipt_WithoutAnyClientSideBookkeeping()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "queue-lookup",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(5)
            )
        );

        var execution = fx.Services.GetRequiredService<ITrainExecutionService>();

        var receipt = await execution.QueueAsync(
            typeof(ISchedulerTestTrain).FullName!,
            """{"value":"find-me"}""",
            ct: default
        );

        await fx.RunJobDispatcherAsync();
        fx.DataContext.Reset();

        // The whole reconciliation path, from the only two values the mutation returned.
        var byWorkQueueId = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .FirstAsync(w => w.Id == receipt.WorkQueueId);
        var byExternalId = await fx
            .DataContext.Metadatas.AsNoTracking()
            .SingleAsync(m => m.ExternalId == receipt.ExternalId);

        byWorkQueueId.MetadataId.Should().Be(byExternalId.Id);
    }
}
