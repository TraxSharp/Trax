using System.Text.Json;
using Trax.Effect.Enums;
using Trax.Samples.ContentShield.E2E.Fixtures;
using Trax.Samples.ContentShield.E2E.Utilities;

namespace Trax.Samples.ContentShield.E2E.ApiTests;

/// <summary>
/// The runner publishes lifecycle events to RabbitMQ, so a subscriber on the API sees a run the
/// runner finished. Only the moderator trains are broadcast: an anonymous review's result goes to
/// nobody's subscription.
/// </summary>
[TestFixture]
public class CrossProcessEventTests : ApiTestFixture
{
    private static Task<GraphQLWebSocketClient> SubscribeAsModeratorAsync() =>
        SubscribeAsync(ModeratorKey);

    private static async Task<GraphQLWebSocketClient> SubscribeAsync(string apiKey)
    {
        // With API-key auth registered every socket must carry a credential.
        var socket = await GraphQLWebSocketClient.ConnectAsync(
            SharedApiSetup.Factory.Server.CreateWebSocketClient(),
            apiKey: apiKey
        );
        await socket.SubscribeAsync(
            "completed",
            "subscription { onTrainCompleted { externalId trainName output } }"
        );
        return socket;
    }

    /// <summary>Queues a violation notice as a moderator and returns its external id.</summary>
    private async Task<string> QueueNoticeAsync(string contentId)
    {
        var queued = await GetGraphQLClient()
            .SendAsync(
                $$"""
                mutation {
                    dispatch {
                        sendViolationNotice(
                            input: { contentId: "{{contentId}}", violationType: "spam", userId: "u-1" }
                        ) { externalId }
                    }
                }
                """,
                apiKey: ModeratorKey
            );
        queued.HasErrors.Should().BeFalse(queued.FirstErrorMessage);
        return queued
            .GetData("dispatch", "sendViolationNotice")
            .GetProperty("externalId")
            .GetString()!;
    }

    /// <summary>
    /// Reads completions until the one for <paramref name="externalId"/> arrives, and returns it
    /// with every completion that came before it.
    /// </summary>
    private static async Task<(JsonElement Event, List<JsonElement> Before)> WaitForAsync(
        GraphQLWebSocketClient socket,
        string externalId
    )
    {
        var before = new List<JsonElement>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            remaining.Should().BePositive("the runner's completion should reach the API");

            var payload = await socket.ReceiveNextAsync(remaining);
            if (!payload.TryGetProperty("data", out var data))
                continue;
            var completed = data.GetProperty("onTrainCompleted");
            if (completed.GetProperty("externalId").GetString() == externalId)
                return (completed, before);
            before.Add(completed);
        }
    }

    [Test]
    public async Task A_notice_the_runner_sends_reaches_a_subscriber_on_the_api()
    {
        await using var socket = await SubscribeAsModeratorAsync();

        var externalId = await QueueNoticeAsync("e2e-event");

        var (completed, _) = await WaitForAsync(socket, externalId);
        completed
            .GetProperty("trainName")
            .GetString()
            .Should()
            .EndWith("ISendViolationNoticeTrain");
        completed.GetProperty("output").GetRawText().Should().Contain("noticeId");
    }

    [Test]
    public async Task An_anonymous_reviews_result_is_not_broadcast()
    {
        await using var socket = await SubscribeAsModeratorAsync();

        var review = await GetGraphQLClient()
            .SendAsync(
                """
                mutation {
                    dispatch {
                        moderation {
                            reviewContent(
                                input: { contentId: "e2e-private", contentType: "text", contentBody: "hello" }
                            ) { externalId }
                        }
                    }
                }
                """
            );
        review.HasErrors.Should().BeFalse(review.FirstErrorMessage);
        var reviewId = review
            .GetData("dispatch", "moderation", "reviewContent")
            .GetProperty("externalId")
            .GetString();
        await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "ReviewContent",
            TrainState.Completed
        );

        // The review finished first, so had its result been published, it would arrive before the
        // notice queued after it.
        var (_, before) = await WaitForAsync(socket, await QueueNoticeAsync("e2e-fence"));

        before
            .Should()
            .NotContain(
                e => e.GetProperty("externalId").GetString() == reviewId,
                "anyone may review content, so its results are not broadcast to subscribers"
            );
    }
}
