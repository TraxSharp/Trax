using Trax.Samples.ChatService.E2E.Fixtures;
using Trax.Samples.ChatService.E2E.Utilities;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

/// <summary>
/// Trax's own lifecycle subscriptions on the same socket as <c>onChatEvent</c>. No chat train is
/// <c>[TraxBroadcast]</c>: a broadcast train's lifecycle events carry every run's output to every
/// caller the train admits, so broadcasting a chat train would hand every user every room's
/// messages. With nothing broadcast, a user's lifecycle subscription is refused outright.
/// <c>ChatEventSubscriptionTests</c> covers the room-scoped feed that replaces it.
/// </summary>
[TestFixture]
public class LifecycleSubscriptionTests : ChatApiTestFixture
{
    [TestCase("onTrainStarted")]
    [TestCase("onTrainCompleted")]
    [TestCase("onTrainFailed")]
    [TestCase("onTrainCancelled")]
    [TestCase("onTrainStateChanged")]
    public async Task A_user_is_refused_every_lifecycle_subscription(string field)
    {
        var wsClient = SharedChatApiSetup.Factory.Server.CreateWebSocketClient();
        await using var sub = await GraphQLWebSocketClient.ConnectAsync(wsClient, apiKey: AliceKey);

        await sub.SubscribeAsync("lifecycle-1", $"subscription {{ {field} {{ trainName }} }}");

        var answer = await sub.ReceiveAnyAsync();
        answer
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("error", "no chat train is broadcast, so a user may see none of their events");
        answer.GetRawText().Should().Contain("Not authorized.");
    }
}
