using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The page uses the operator key because a subscriber outside the operations view sees a run's
/// shape but not its answers, nor the names of the steps on a track.
/// </summary>
[TestFixture]
public class BroadcastViewTests : RecoveryTestFixture
{
    [Test]
    public async Task ViewerSubscriber_SeesTheShape_ButNotTheAnswersOrTheTrack()
    {
        // A host with a token scheme refuses a socket without a credential, so the broadcast view
        // is a key without the Operator role.
        await using var viewer = await JunctionEventStream.ConnectAsync(
            SharedRecoverySetup.Factory.Server.CreateWebSocketClient(),
            DemoKeys.Viewer
        );

        // onJunctionEvent does not replay and says nothing when a subscription starts, so the run
        // waits at its second question until the viewer has received one of its steps. The
        // subscription is then live before the routing step the test reads.
        long first;
        using (Decider.Hold("ApproveRefund"))
        {
            await Run.StartAsync("REFUND", crashOnce: false, orderId: "A-1001");
            first = await Run.FollowAttemptAsync(1);
            await viewer.SubscribeAsync(first);
            var live = await Trax.Samples.Shared.Testing.Polling.WaitUntilAsync(
                () => viewer.StepsOf(first).Count > 0,
                TimeSpan.FromSeconds(10)
            );
            live.Should()
                .BeTrue("the viewer should receive a step before the run's second question");
        }
        (await Run.WaitForEndAsync(first)).Should().Be("COMPLETED");

        var seen = await Trax.Samples.Shared.Testing.Polling.WaitUntilAsync(
            () =>
                viewer.StepsOf(first).Any(e => e.GetProperty("eventType").GetString() == "ROUTED"),
            TimeSpan.FromSeconds(10)
        );
        seen.Should().BeTrue("the viewer should receive the run's steps");

        var steps = viewer.StepsOf(first).Select(e => e.GetProperty("junction")).ToList();
        Questions(steps)
            .Should()
            .OnlyContain(q =>
                q.GetProperty("answer").ValueKind == System.Text.Json.JsonValueKind.Null
            );
        steps
            .Where(s =>
                s.GetProperty("trackPosition").ValueKind == System.Text.Json.JsonValueKind.Number
            )
            .Should()
            .OnlyContain(s =>
                s.GetProperty("nameWithheld").GetBoolean()
                && s.GetProperty("name").GetString() == "(withheld)"
            );

        // The operator saw the same steps by name.
        Names(await Run.TimelineAsync(first)).Should().Contain("IssuePayment");
    }
}
