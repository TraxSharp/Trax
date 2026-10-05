using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// A run whose last step crashes once recovers on the manifest's automatic retry, and the retry
/// replays the first attempt's decisions instead of asking the model again.
/// </summary>
[TestFixture]
public class RetryReplayTests : RecoveryTestFixture
{
    [Test]
    public async Task ResearchRun_CrashedOnce_CompletesOnAttempt2_WithoutAskingTheModelAgain()
    {
        await Run.StartAsync("RESEARCH", crashOnce: true);

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("FAILED");
        var second = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");

        // The model was asked each question once, across both attempts.
        Decider.Asked(Run.RunId, "Source").Should().Be(1);
        Decider.Asked(Run.RunId, "Depth").Should().Be(1);

        var attempt1 = await Run.TimelineAsync(first);
        var attempt2 = await Run.TimelineAsync(second);

        attempt1.Should().OnlyContain(s => Attempt(s) == 1);
        attempt2.Should().OnlyContain(s => Attempt(s) == 2);

        // Attempt 1 asked both questions and failed writing the report.
        Questions(attempt1).Should().OnlyContain(q => !q.GetProperty("replayed").GetBoolean());
        attempt1.Last().GetProperty("name").GetString().Should().Be("Summarize");
        attempt1
            .Last()
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("FAILED", "the armed crash fires in the report step");

        // Attempt 2 replayed both answers, took the same tracks, and ran to the end.
        Questions(attempt2).Should().HaveCount(2);
        Questions(attempt2).Should().OnlyContain(q => q.GetProperty("replayed").GetBoolean());
        Names(attempt2)
            .Should()
            .Equal(
                "PlanResearch",
                "Source",
                "Source",
                "SearchPapers",
                "Depth",
                "Depth",
                "FetchFullTexts",
                "Summarize"
            );

        // The subscription itself carried the replayed decisions, with their answers.
        Questions(Run.LiveSteps(second))
            .Should()
            .Contain(q =>
                q.GetProperty("replayed").GetBoolean()
                && q.GetProperty("questionKey").GetString() == "Source"
                && q.GetProperty("answer").GetString() == "Papers"
            );

        var journal = await Run.JournalAsync(second);
        journal.GetProperty("replayDecisionsOf").GetInt64().Should().Be(first);
        journal
            .GetProperty("decisions")
            .EnumerateArray()
            .Should()
            .OnlyContain(d => d.GetProperty("replayed").GetBoolean());
        Stream.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task RefundRun_PaymentTimedOutOnce_RetryTakesTheSameTrackWithoutAskingAgain()
    {
        await Run.StartAsync("REFUND", crashOnce: true, orderId: "A-1001");

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("FAILED");
        var second = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");

        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(1);

        var attempt1 = await Run.TimelineAsync(first);
        attempt1.Last().GetProperty("name").GetString().Should().Be("IssuePayment");
        attempt1.Last().GetProperty("state").GetString().Should().Be("FAILED");

        var attempt2 = await Run.TimelineAsync(second);
        Questions(attempt2)
            .Should()
            .ContainSingle()
            .Which.GetProperty("replayed")
            .GetBoolean()
            .Should()
            .BeTrue();
        Names(attempt2)
            .Should()
            .Equal(
                "LoadRefundCase",
                "ApproveRefund",
                "ApproveRefund",
                "IssuePayment",
                "NotifyCustomer"
            );

        // The state holds a [TraxSensitive] member, so the hash is keyed (k1:) by the dev key.
        var journal = await Run.JournalAsync(second);
        journal
            .GetProperty("decisions")[0]
            .GetProperty("stateHash")
            .GetString()
            .Should()
            .StartWith("k1:");
    }

    [TestCase("A-1002", "QueueForReview", "QueuedForReview")]
    [TestCase("A-1003", "DeclineRefund", "Declined")]
    public async Task RefundRun_OnAnotherTrack_CrashesOnce_AndTheRetryReplaysTheDecision(
        string orderId,
        string track,
        string status
    )
    {
        await Run.StartAsync("REFUND", crashOnce: true, orderId: orderId);

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("FAILED", "the page offers a crash here");
        var second = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");

        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(1);

        var attempt1 = await Run.TimelineAsync(first);
        attempt1.Last().GetProperty("name").GetString().Should().Be(track);
        attempt1.Last().GetProperty("state").GetString().Should().Be("FAILED");

        var attempt2 = await Run.TimelineAsync(second);
        Questions(attempt2)
            .Should()
            .ContainSingle()
            .Which.GetProperty("replayed")
            .GetBoolean()
            .Should()
            .BeTrue();
        Names(attempt2)
            .Should()
            .Equal("LoadRefundCase", "ApproveRefund", "ApproveRefund", track, "NotifyCustomer");

        var detail = await GraphQL.SendAsync(
            $$"""{ operations { executionDetail(id: {{second}}) { output } } }""",
            OperatorKey
        );
        detail.HasErrors.Should().BeFalse(detail.FirstErrorMessage);
        detail
            .GetData("operations", "executionDetail", "output")
            .GetString()
            .Should()
            .Contain(status, "the retry finished the track the first attempt was on");
    }

    [Test]
    public async Task RunWithNoCrashArmed_CompletesInOneAttempt()
    {
        await Run.StartAsync("REFUND", crashOnce: false, orderId: "A-1001");

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("COMPLETED");

        // A one-off manifest disables itself after a success, so nothing more can be queued for it.
        var disabled = await Polling.WaitUntilAsync(
            async () =>
            {
                var response = await GraphQL.SendAsync(
                    $$"""{ operations { manifest(id: {{Run.ManifestId}}) { isEnabled } } }""",
                    OperatorKey
                );
                return !response.GetData("operations", "manifest", "isEnabled").GetBoolean();
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(100)
        );
        disabled.Should().BeTrue("a once manifest disables itself after its run succeeds");
        (await Run.ExecutionIdsAsync()).Should().Equal(first);

        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(1);
        var timeline = await Run.TimelineAsync(first);
        timeline.Should().OnlyContain(s => Attempt(s) == 1);
        Questions(timeline).Should().OnlyContain(q => !q.GetProperty("replayed").GetBoolean());
    }
}
