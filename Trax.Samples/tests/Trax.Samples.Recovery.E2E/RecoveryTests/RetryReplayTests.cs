using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// A refund run that crashes once recovers on the manifest's automatic retry without asking the model
/// again. Crashed while it pays, before its checkpoint, the retry runs the chain again and replays both
/// of the first attempt's answers. Crashed while it emails, after the checkpoint, the retry starts from
/// the stored outcome and runs only the email.
/// </summary>
[TestFixture]
public class RetryReplayTests : RecoveryTestFixture
{
    [Test]
    public async Task RefundRun_CrashedWhileEmailing_RetryStartsFromTheCheckpoint()
    {
        await Run.StartAsync("REFUND", crashOnce: true, orderId: "A-1001", crashWhile: "EMAILING");

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("FAILED");
        var second = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");

        // The model was asked each question once, across both attempts.
        Decider.Asked(Run.RunId, "Problem").Should().Be(1);
        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(1);

        var attempt1 = await Run.TimelineAsync(first);
        var attempt2 = await Run.TimelineAsync(second);
        attempt1.Should().OnlyContain(s => Attempt(s) == 1);
        attempt2.Should().OnlyContain(s => Attempt(s) == 2);

        // Attempt 1 asked both questions, paid, checkpointed the outcome and failed emailing.
        Questions(attempt1).Should().HaveCount(2);
        Names(attempt1).Should().Contain("IssuePayment");
        attempt1.Last().GetProperty("name").GetString().Should().Be("NotifyCustomer");
        attempt1.Last().GetProperty("state").GetString().Should().Be("FAILED");

        // Attempt 2 started from the checkpoint: it paid nothing again and asked nothing.
        Names(attempt2)
            .Should()
            .Equal(
                ["NotifyCustomer"],
                "a manifest's retry resumes after the failed run's latest checkpoint"
            );
        var journal = await Run.JournalAsync(second);
        journal
            .GetProperty("decisions")
            .EnumerateArray()
            .Should()
            .BeEmpty("no question comes after the checkpoint");
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

        Decider.Asked(Run.RunId, "Problem").Should().Be(1);
        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(1);

        var attempt1 = await Run.TimelineAsync(first);
        attempt1.Last().GetProperty("name").GetString().Should().Be("IssuePayment");
        attempt1.Last().GetProperty("state").GetString().Should().Be("FAILED");

        var attempt2 = await Run.TimelineAsync(second);
        Questions(attempt2)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(q => q.GetProperty("replayed").GetBoolean());
        Names(attempt2)
            .Should()
            .Equal(
                "LoadRefundCase",
                "Problem",
                "Problem",
                "RequestPhotos",
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

    [TestCase("A-1002", "TraceParcel", "QueueForReview", "QueuedForReview")]
    [TestCase("A-1003", "CheckReturnWindow", "DeclineRefund", "Declined")]
    public async Task RefundRun_OnOtherTracks_CrashesOnce_AndTheRetryReplaysTheDecisions(
        string orderId,
        string check,
        string track,
        string status
    )
    {
        await Run.StartAsync("REFUND", crashOnce: true, orderId: orderId);

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("FAILED", "the page offers a crash here");
        var second = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");

        Decider.Asked(Run.RunId, "Problem").Should().Be(1);
        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(1);

        var attempt1 = await Run.TimelineAsync(first);
        attempt1.Last().GetProperty("name").GetString().Should().Be(track);
        attempt1.Last().GetProperty("state").GetString().Should().Be("FAILED");

        var attempt2 = await Run.TimelineAsync(second);
        Questions(attempt2)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(q => q.GetProperty("replayed").GetBoolean());
        Names(attempt2)
            .Should()
            .Equal(
                "LoadRefundCase",
                "Problem",
                "Problem",
                check,
                "ApproveRefund",
                "ApproveRefund",
                track,
                "NotifyCustomer"
            );

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
                    $$"""{ operations { manifestDetail(id: {{Run.ManifestId}}) { isEnabled } } }""",
                    OperatorKey
                );
                return !response.GetData("operations", "manifestDetail", "isEnabled").GetBoolean();
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(100)
        );
        disabled.Should().BeTrue("a once manifest disables itself after its run succeeds");
        (await Run.ExecutionIdsAsync()).Should().Equal(first);

        Decider.Asked(Run.RunId, "Problem").Should().Be(1);
        Decider.Asked(Run.RunId, "ApproveRefund").Should().Be(1);
        var timeline = await Run.TimelineAsync(first);
        timeline.Should().OnlyContain(s => Attempt(s) == 1);
        Questions(timeline).Should().OnlyContain(q => !q.GetProperty("replayed").GetBoolean());
    }
}
