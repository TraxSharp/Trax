using System.Text.Json;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class SubscriptionTests : ApiTestFixture
{
    private const string LookupPlayerQuery = """
        {
            discover {
                players {
                    lookupPlayer(input: { playerId: "player-1" }) {
                        playerId
                    }
                }
            }
        }
        """;

    [Test]
    public async Task OnTrainCompleted_ReceivesEvent()
    {
        var wsClient = SharedApiSetup.Factory.Server.CreateWebSocketClient();
        await using var sub = await GraphQLWebSocketClient.ConnectAsync(
            wsClient,
            apiKey: PlayerKey
        );

        await sub.SubscribeAsync(
            "completed-1",
            """
            subscription {
                onTrainCompleted {
                    metadataId
                    trainName
                    trainState
                }
            }
            """
        );

        var data = await TriggerUntilReceivedAsync(sub, "onTrainCompleted");

        data.GetProperty("metadataId").GetInt64().Should().BeGreaterThan(0);
        data.GetProperty("trainName").GetString().Should().Contain("LookupPlayer");
        data.GetProperty("trainState").GetString().Should().Be("COMPLETED");
    }

    [Test]
    public async Task OnTrainStarted_ReceivesEvent()
    {
        var wsClient = SharedApiSetup.Factory.Server.CreateWebSocketClient();
        await using var sub = await GraphQLWebSocketClient.ConnectAsync(
            wsClient,
            apiKey: PlayerKey
        );

        await sub.SubscribeAsync(
            "started-1",
            """
            subscription {
                onTrainStarted {
                    metadataId
                    trainName
                    trainState
                }
            }
            """
        );

        var data = await TriggerUntilReceivedAsync(sub, "onTrainStarted");

        data.GetProperty("metadataId").GetInt64().Should().BeGreaterThan(0);
        data.GetProperty("trainName").GetString().Should().Contain("LookupPlayer");
    }

    [Test]
    public async Task OnTrainFailed_NoEventForNonBroadcastTrain()
    {
        var wsClient = SharedApiSetup.Factory.Server.CreateWebSocketClient();
        await using var sub = await GraphQLWebSocketClient.ConnectAsync(
            wsClient,
            apiKey: PlayerKey
        );

        await sub.SubscribeAsync(
            "failed-1",
            """
            subscription {
                onTrainFailed {
                    metadataId
                    trainName
                }
            }
            """
        );

        // Nothing is triggered, so no onTrainFailed event should ever arrive. The window is a
        // negative-wait: it must elapse to confirm the absence of an event. A not-yet-registered
        // subscription also produces no event, so this assertion cannot be raced into a false pass.
        var received = await sub.TryReceiveNextAsync(TimeSpan.FromSeconds(3));
        received.Should().BeFalse("non-broadcast trains should not emit subscription events");
    }

    /// <summary>
    /// The graphql-transport-ws protocol has no acknowledgement that a <c>subscribe</c> has been
    /// registered server-side, so a single trigger fired immediately after subscribing can broadcast
    /// before the topic is live and the event is lost. Rather than racing (or sleeping for a guessed
    /// interval), re-trigger the broadcast train until the subscription delivers an event, with a
    /// generous overall ceiling that fails loudly if it never does.
    /// </summary>
    private async Task<JsonElement> TriggerUntilReceivedAsync(
        GraphQLWebSocketClient sub,
        string subscriptionField
    )
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var trigger = await GraphQL.SendAsync(LookupPlayerQuery, apiKey: PlayerKey);
            trigger
                .HasErrors.Should()
                .BeFalse($"trigger query failed: {trigger.FirstErrorMessage}");

            try
            {
                var payload = await sub.ReceiveNextAsync(TimeSpan.FromSeconds(2));
                return payload.GetProperty("data").GetProperty(subscriptionField);
            }
            catch (TimeoutException)
            {
                // Subscription not registered yet (or this trigger raced it); trigger again.
            }
        }

        throw new TimeoutException(
            $"No '{subscriptionField}' subscription event received after re-triggering for 30s."
        );
    }
}
