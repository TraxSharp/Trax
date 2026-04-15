using Trax.Samples.ChatService.E2E.Fixtures;
using Trax.Samples.ChatService.E2E.Utilities;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

/// <summary>
/// Tests for the custom onChatEvent subscription.
/// Uses the built-in Trax lifecycle subscriptions (onTrainCompleted, onTrainStarted)
/// since the ChatLifecycleHook publishes events to both the custom ChatRoom topic
/// and triggers the standard Trax lifecycle events for [TraxBroadcast] trains.
/// </summary>
[TestFixture]
public class SubscriptionTests : ChatApiTestFixture
{
    private async Task<string> CreateRoom()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Sub Test Room", userId: "alice", displayName: "Alice" }
                    ) {
                        output { chatRoomId }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();
        return result
            .GetData("dispatch", "createChatRoom", "output")
            .GetProperty("chatRoomId")
            .GetString()!;
    }

    [Test]
    public async Task OnTrainCompleted_ReceivesEventForSendMessage()
    {
        var chatRoomId = await CreateRoom();

        var wsClient = SharedChatApiSetup.Factory.Server.CreateWebSocketClient();
        await using var sub = await GraphQLWebSocketClient.ConnectAsync(wsClient, apiKey: AliceKey);

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

        // Send a message (SendMessage has [TraxBroadcast]).
        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    sendMessage(
                        input: { chatRoomId: "{{chatRoomId}}", senderUserId: "alice", content: "Sub test!" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        var payload = await sub.ReceiveNextAsync(TimeSpan.FromSeconds(10));
        var data = payload.GetProperty("data").GetProperty("onTrainCompleted");

        data.GetProperty("trainName").GetString().Should().Contain("SendMessage");
        data.GetProperty("trainState").GetString().Should().Be("COMPLETED");
    }

    [Test]
    public async Task OnTrainCompleted_ReceivesEventForCreateChatRoom()
    {
        var wsClient = SharedChatApiSetup.Factory.Server.CreateWebSocketClient();
        await using var sub = await GraphQLWebSocketClient.ConnectAsync(wsClient, apiKey: AliceKey);

        await sub.SubscribeAsync(
            "completed-2",
            """
            subscription {
                onTrainCompleted {
                    metadataId
                    trainName
                }
            }
            """
        );

        // Create a room (CreateChatRoom has [TraxBroadcast]).
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Sub Room", userId: "alice", displayName: "Alice" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        var payload = await sub.ReceiveNextAsync(TimeSpan.FromSeconds(10));
        var data = payload.GetProperty("data").GetProperty("onTrainCompleted");

        data.GetProperty("trainName").GetString().Should().Contain("CreateChatRoom");
    }

    [Test]
    public async Task MarkChatAsRead_DoesNotTriggerSubscription()
    {
        var chatRoomId = await CreateRoom();

        var wsClient = SharedChatApiSetup.Factory.Server.CreateWebSocketClient();
        await using var sub = await GraphQLWebSocketClient.ConnectAsync(wsClient, apiKey: AliceKey);

        await sub.SubscribeAsync(
            "no-event-1",
            """
            subscription {
                onTrainCompleted {
                    trainName
                }
            }
            """
        );

        // Small delay to ensure subscription is established.
        await Task.Delay(500);

        // MarkChatAsRead does NOT have [TraxBroadcast].
        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    markChatAsRead(
                        input: { chatRoomId: "{{chatRoomId}}", userId: "alice" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        var received = await sub.TryReceiveNextAsync(TimeSpan.FromSeconds(3));
        received.Should().BeFalse("MarkChatAsRead does not have [TraxBroadcast]");
    }
}
