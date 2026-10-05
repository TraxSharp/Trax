using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;
using Trax.Samples.Scheduling.E2E.Utilities;

namespace Trax.Samples.Scheduling.E2E.OperationsTests;

/// <summary>
/// Resolving a dead letter, over the GraphQL operations surface and through
/// <c>ITraxScheduler</c>. Each test first drives <c>import-supplier-feed</c> through a fresh
/// outage to a new dead letter; the outage is over by then, so a requeued run succeeds.
/// </summary>
public class DeadLetterOperationsTests : SchedulingTestFixture
{
    [Test]
    public async Task Requeue_over_GraphQL_runs_the_manifest_again_and_links_the_run()
    {
        var outage = await SupplierOutage.RunToDeadLetter(Db, SupplierFeed, Scheduler);
        var marker = await Db.LatestMetadataId();

        var response = await Operator.Send(
            $$"""
            mutation {
              operations {
                deadLetters {
                  requeueDeadLetter(id: {{outage.DeadLetter.Id}}) { success workQueueId message }
                }
              }
            }
            """
        );

        var result = response.Data("operations", "deadLetters", "requeueDeadLetter");
        result.GetProperty("success").GetBoolean().Should().BeTrue(response.Body);
        result.GetProperty("workQueueId").GetInt64().Should().BePositive();

        var rerun = await Db.WaitForRun(
            outage.ManifestId,
            TrainState.Completed,
            marker,
            TimeSpan.FromSeconds(30)
        );

        await Db.WaitUntil(
            async () =>
                await Db.Query(dc =>
                    dc.DeadLetters.AnyAsync(d =>
                        d.Id == outage.DeadLetter.Id
                        && d.Status == DeadLetterStatus.Retried
                        && d.RetryMetadataId == rerun.Id
                    )
                ),
            TimeSpan.FromSeconds(10),
            "the dead letter is marked Retried and linked to the run its requeue started"
        );
    }

    [Test]
    public async Task Requeue_through_ITraxScheduler_queues_an_entry_that_names_the_dead_letter()
    {
        var outage = await SupplierOutage.RunToDeadLetter(Db, SupplierFeed, Scheduler);

        var result = await Scheduler.RequeueDeadLetterAsync(outage.DeadLetter.Id);

        result.Success.Should().BeTrue(result.Message);
        var entry = await Db.Query(dc =>
            dc.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == result.WorkQueueId)
        );
        entry.DeadLetterId.Should().Be(outage.DeadLetter.Id);
        entry.ManifestId.Should().Be(outage.ManifestId);

        await Db.WaitForRun(
            outage.ManifestId,
            TrainState.Completed,
            outage.Failures[^1].Id,
            TimeSpan.FromSeconds(30)
        );
    }

    [Test]
    public async Task Acknowledge_over_GraphQL_resolves_without_running_and_a_later_requeue_is_refused()
    {
        var outage = await SupplierOutage.RunToDeadLetter(Db, SupplierFeed, Scheduler);

        var acknowledge = await Operator.Send(
            $$"""
            mutation {
              operations {
                deadLetters {
                  acknowledgeDeadLetter(id: {{outage.DeadLetter.Id}}, note: "Supplier confirmed the outage") {
                    success
                    message
                  }
                }
              }
            }
            """
        );
        acknowledge
            .Data("operations", "deadLetters", "acknowledgeDeadLetter")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(acknowledge.Body);

        var resolved = await Db.Query(dc =>
            dc.DeadLetters.AsNoTracking().SingleAsync(d => d.Id == outage.DeadLetter.Id)
        );
        resolved.Status.Should().Be(DeadLetterStatus.Acknowledged);
        resolved.ResolutionNote.Should().Be("Supplier confirmed the outage");
        resolved.RetryMetadataId.Should().BeNull();

        var requeue = await Scheduler.RequeueDeadLetterAsync(outage.DeadLetter.Id);
        requeue
            .Success.Should()
            .BeFalse("only a dead letter awaiting intervention can be requeued");
        requeue.WorkQueueId.Should().BeNull();
    }

    [Test]
    public async Task Dead_letters_query_lists_the_awaiting_dead_letter()
    {
        var outage = await SupplierOutage.RunToDeadLetter(Db, SupplierFeed, Scheduler);

        var response = await Operator.Send(
            """
            {
              operations {
                deadLetters {
                  deadLetters(status: AWAITING_INTERVENTION, take: 10) {
                    items { id manifestId manifestName status reason retryCountAtDeadLetter }
                    totalCount
                  }
                }
              }
            }
            """
        );

        var items = response
            .Data("operations", "deadLetters", "deadLetters", "items")
            .EnumerateArray()
            .ToList();
        var listed = items.Single(i => i.GetProperty("id").GetInt64() == outage.DeadLetter.Id);
        listed.GetProperty("manifestId").GetInt64().Should().Be(outage.ManifestId);
        listed.GetProperty("status").GetString().Should().Be("AWAITING_INTERVENTION");
        listed.GetProperty("retryCountAtDeadLetter").GetInt32().Should().Be(3);
    }
}
