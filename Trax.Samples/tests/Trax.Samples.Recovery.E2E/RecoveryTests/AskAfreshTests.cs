using Trax.Samples.Recovery.E2E.Fixtures;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// When replaying is not what the operator wants, the retry asks the model again: on purpose with
/// <c>askAfresh</c>, or by itself when the data the decision was about changed during the backoff.
/// </summary>
[TestFixture]
public class AskAfreshTests : RecoveryTestFixture
{
    [Test]
    public async Task TriggerWithAskAfresh_DuringTheBackoff_RetryAsksTheModelAgain()
    {
        await Run.StartAsync("RESEARCH", crashOnce: true);

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("FAILED");

        var trigger = await GraphQL.SendAsync(
            $$"""
            mutation { operations {
              triggerManifest(externalId: "{{Run.ManifestExternalId}}", askAfresh: true) { success message }
            } }
            """,
            OperatorKey
        );
        trigger.HasErrors.Should().BeFalse(trigger.FirstErrorMessage);
        var result = trigger.GetData("operations", "triggerManifest");
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("message").GetString().Should().NotContain("already claimed");

        var second = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");

        Decider.Asked(Run.RunId, "Source").Should().Be(2);
        Decider.Asked(Run.RunId, "Depth").Should().Be(2);

        var attempt2 = await Run.TimelineAsync(second);
        Questions(attempt2).Should().HaveCount(2);
        Questions(attempt2).Should().OnlyContain(q => !q.GetProperty("replayed").GetBoolean());

        var journal = await Run.JournalAsync(second);
        journal
            .GetProperty("replayDecisionsOf")
            .ValueKind.Should()
            .Be(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    public async Task RequeueExecution_ReplaysByDefault_AndAsksAgainWithAskAfresh()
    {
        await Run.StartAsync("REFUND", crashOnce: false, orderId: "A-1001");
        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("COMPLETED");
        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(1);

        var replayed = await RequeueAsync(first, askAfresh: false);
        (await Run.WaitForEndAsync(replayed)).Should().Be("COMPLETED");
        Decider
            .Asked(Run.RunId, "ApproveRefund")
            .Should()
            .Be(1, "a requeue replays the run's answers");
        (await Run.JournalAsync(replayed))
            .GetProperty("decisions")[0]
            .GetProperty("replayed")
            .GetBoolean()
            .Should()
            .BeTrue();

        var afresh = await RequeueAsync(first, askAfresh: true);
        (await Run.WaitForEndAsync(afresh)).Should().Be("COMPLETED");
        Decider
            .Asked(Run.RunId, "ApproveRefund")
            .Should()
            .Be(2, "askAfresh puts the question to the model again");
    }

    [Test]
    public async Task DataChangedDuringTheBackoff_RetryAsksAfresh_AndTakesTheTrackTheNewDataCallsFor()
    {
        await Run.StartAsync("REFUND", crashOnce: true, orderId: "A-1001");

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("FAILED");

        var change = await GraphQL.SendAsync(
            $$"""
            mutation { dispatch { changeCaseData(input: { runId: "{{Run.RunId}}" }) { output { change } } } }
            """,
            OperatorKey
        );
        change.HasErrors.Should().BeFalse(change.FirstErrorMessage);

        var second = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");

        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(2);

        var attempt1 = await Run.TimelineAsync(first);
        var attempt2 = await Run.TimelineAsync(second);
        Questions(attempt2)
            .Should()
            .ContainSingle()
            .Which.GetProperty("replayed")
            .GetBoolean()
            .Should()
            .BeFalse();
        Names(attempt1).Should().Contain("IssuePayment");
        Names(attempt2).Should().Contain("QueueForReview").And.NotContain("IssuePayment");

        // The retry was still queued to replay attempt 1; the replay refused the answer because the
        // state it was given about no longer hashes the same. The run-level replay was honoured, so it
        // is not marked replay_abandoned.
        var journal = await Run.JournalAsync(second);
        journal.GetProperty("replayDecisionsOf").GetInt64().Should().Be(first);
        journal.GetProperty("replayAbandoned").GetBoolean().Should().BeFalse();
        var decision = journal.GetProperty("decisions")[0];
        decision.GetProperty("replayed").GetBoolean().Should().BeFalse();
        decision.GetProperty("replayRefused").GetString().Should().Contain("different state");
    }

    private async Task<long> RequeueAsync(long metadataId, bool askAfresh)
    {
        var response = await GraphQL.SendAsync(
            $$"""
            mutation { operations {
              requeueExecution(id: {{metadataId}}, askAfresh: {{(
                askAfresh ? "true" : "false"
            )}}) { success message id }
            } }
            """,
            OperatorKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var result = response.GetData("operations", "requeueExecution");
        result
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(result.GetProperty("message").GetString());

        // The response's id is the work queue entry; the run's id appears once it is dispatched.
        var entryId = result.GetProperty("id").GetInt64();
        long runId = 0;
        var dispatched = await Trax.Samples.Shared.Testing.Polling.WaitUntilAsync(
            async () =>
            {
                var entry = await GraphQL.SendAsync(
                    $$"""{ operations { workQueue { workQueue(id: {{entryId}}) { metadataId } } } }""",
                    OperatorKey
                );
                var id = entry.GetData("operations", "workQueue", "workQueue", "metadataId");
                if (id.ValueKind != System.Text.Json.JsonValueKind.Number)
                    return false;
                runId = id.GetInt64();
                return true;
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(100)
        );
        dispatched.Should().BeTrue("the requeued entry should be dispatched");
        return runId;
    }
}
