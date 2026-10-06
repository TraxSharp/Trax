using System.Text.Json;
using Trax.Samples.ChatService.E2E.Fixtures;
using Trax.Samples.ChatService.E2E.Utilities;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

/// <summary>
/// The sample's headline feature: <c>subscription { onChatEvent(chatRoomId:) }</c> delivers a
/// room's new messages over the WebSocket to the room's participants, and to nobody else.
/// </summary>
[TestFixture]
public class ChatEventSubscriptionTests : ChatApiTestFixture
{
    private static string OnChatEvent(string roomId) =>
        $$"""
            subscription { onChatEvent(chatRoomId: "{{roomId}}") { chatRoomId eventType payload } }
            """;

    private static async Task<GraphQLWebSocketClient> ConnectAsync(string? apiKey) =>
        await GraphQLWebSocketClient.ConnectAsync(
            SharedChatApiSetup.Factory.Server.CreateWebSocketClient(),
            apiKey: apiKey
        );

    /// <summary>
    /// Sends messages as <paramref name="senderKey"/> until <paramref name="sub"/> receives one, and
    /// returns the event. Retrying covers the moment between the subscribe message and the server
    /// registering it, without a fixed delay.
    /// </summary>
    private async Task<JsonElement> SendUntilReceivedAsync(
        GraphQLWebSocketClient sub,
        string senderKey,
        string roomId,
        string content
    )
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var sent = await SendMessageAsAsync(senderKey, roomId, $"{content} {attempt}");
            sent.HasErrors.Should().BeFalse(sent.FirstErrorMessage);
            try
            {
                return (await sub.ReceiveNextAsync(TimeSpan.FromSeconds(1)))
                    .GetProperty("data")
                    .GetProperty("onChatEvent");
            }
            catch (TimeoutException)
            {
                // Not registered yet: send again.
            }
        }
        throw new AssertionException("no onChatEvent arrived");
    }

    /// <summary>What the server answered a refused subscription with: a protocol message, or a close.</summary>
    private static async Task<string> ReceiveRefusalAsync(GraphQLWebSocketClient sub)
    {
        try
        {
            var message = await sub.ReceiveAnyAsync();
            message.GetProperty("type").GetString().Should().NotBe("complete");
            return message.GetRawText();
        }
        catch (WebSocketClosedException closed)
        {
            return $"closed {(int?)closed.Status} {closed.Description}";
        }
    }

    [Test]
    public async Task OnChatEvent_is_in_the_served_schema()
    {
        var response = await GraphQL.SendAsync(
            """{ __schema { subscriptionType { name fields { name } } } }"""
        );

        var subscriptionType = response.GetData("__schema", "subscriptionType");
        subscriptionType.GetProperty("name").GetString().Should().Be("LifecycleSubscriptions");
        subscriptionType
            .GetProperty("fields")
            .EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .Should()
            .Contain("onChatEvent")
            .And.NotContain(
                "subscribeToChatEventAsync",
                "the subscribe resolver is not a field of its own"
            );
    }

    [Test]
    public async Task A_participant_receives_onChatEvent_for_a_new_message()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "Live");

        await using var sub = await ConnectAsync(AliceKey);
        await sub.SubscribeAsync("room-1", OnChatEvent(roomId));

        var chatEvent = await SendUntilReceivedAsync(sub, AliceKey, roomId, "hello");

        chatEvent.GetProperty("chatRoomId").GetString().Should().Be(roomId);
        chatEvent.GetProperty("eventType").GetString().Should().Be("MessageSent");
        chatEvent.GetProperty("payload").GetString().Should().Contain("hello");
    }

    [Test]
    public async Task Another_participant_receives_the_message_with_the_senders_identity()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "Pair");
        await JoinRoomAsAsync(BobKey, roomId);

        await using var bob = await ConnectAsync(BobKey);
        await bob.SubscribeAsync("bob-1", OnChatEvent(roomId));

        var chatEvent = await SendUntilReceivedAsync(bob, AliceKey, roomId, "hi bob");

        using var payload = JsonDocument.Parse(chatEvent.GetProperty("payload").GetString()!);
        payload
            .RootElement.GetProperty("senderUserId")
            .GetString()
            .Should()
            .Be("TraxApiKey:alice", "the sender is the authenticated caller");
        payload.RootElement.GetProperty("senderDisplayName").GetString().Should().Be("Alice");
    }

    [Test]
    public async Task A_subscriber_receives_nothing_from_another_room()
    {
        var watched = await CreateRoomAsAsync(AliceKey, "Watched");
        var other = await CreateRoomAsAsync(AliceKey, "Other");

        await using var sub = await ConnectAsync(AliceKey);
        await sub.SubscribeAsync("watched-1", OnChatEvent(watched));
        await SendUntilReceivedAsync(sub, AliceKey, watched, "warm-up");

        // A message in the other room, then one in the watched room: the next event must be the
        // watched room's, because the other room's would arrive first.
        (await SendMessageAsAsync(AliceKey, other, "elsewhere"))
            .HasErrors.Should()
            .BeFalse();
        (await SendMessageAsAsync(AliceKey, watched, "here")).HasErrors.Should().BeFalse();

        var next = (await sub.ReceiveNextAsync(TimeSpan.FromSeconds(10)))
            .GetProperty("data")
            .GetProperty("onChatEvent");
        next.GetProperty("chatRoomId").GetString().Should().Be(watched);
        next.GetProperty("payload").GetString().Should().Contain("here");
    }

    [Test]
    public async Task A_non_participant_is_refused_when_subscribing()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "Private");

        await using var charlie = await ConnectAsync(CharlieKey);
        await charlie.SubscribeAsync("charlie-1", OnChatEvent(roomId));

        var refusal = await ReceiveRefusalAsync(charlie);
        refusal.Should().Contain("Not authorized.");
    }

    [Test]
    public async Task ANonParticipant_ReceivesNoMessageOfAnotherRoom_OnAnyLifecycleSubscription()
    {
        var alicesRoom = await CreateRoomAsAsync(AliceKey, "Alice only");
        var bobsRoom = await CreateRoomAsAsync(BobKey, "Bob only");

        // Bob listens everywhere a message could reach him: Trax's lifecycle fields that carry a
        // run's output, Alice's room, and his own room (which proves the socket is live).
        await using var bob = await ConnectAsync(BobKey);
        string[] lifecycleFields = ["onTrainStarted", "onTrainCompleted", "onTrainStateChanged"];
        foreach (var field in lifecycleFields)
            await bob.SubscribeAsync(field, $"subscription {{ {field} {{ trainName output }} }}");
        await bob.SubscribeAsync("alices-room", OnChatEvent(alicesRoom));
        await bob.SubscribeAsync("bobs-room", OnChatEvent(bobsRoom));

        var received = new List<string>();

        // Warm up until every lifecycle field has answered, by an event or a refusal, and Bob's
        // own room has delivered: from then on, anything Bob may receive reaches him.
        var answered = new HashSet<string>();
        for (
            var attempt = 0;
            attempt < 10 && !answered.IsSupersetOf([.. lifecycleFields, "bobs-room"]);
            attempt++
        )
        {
            (await SendMessageAsAsync(BobKey, bobsRoom, $"warm-up {attempt}"))
                .HasErrors.Should()
                .BeFalse();
            foreach (var message in await DrainAsync(bob, TimeSpan.FromSeconds(1)))
            {
                received.Add(message.GetRawText());
                if (message.TryGetProperty("id", out var id))
                    answered.Add(id.GetString()!);
            }
        }
        answered.Should().Contain([.. lifecycleFields, "bobs-room"], "every subscription answered");

        const string secret = "secret-for-alices-room-only";
        (await SendMessageAsAsync(AliceKey, alicesRoom, secret)).HasErrors.Should().BeFalse();

        // A fence in Bob's room: once it arrives, Alice's message, sent first, has been published.
        (await SendMessageAsAsync(BobKey, bobsRoom, "fence"))
            .HasErrors.Should()
            .BeFalse();
        received.AddRange(
            (await DrainAsync(bob, TimeSpan.FromSeconds(3))).Select(m => m.GetRawText())
        );

        received.Should().Contain(m => m.Contains("fence"), "the fence proves the socket is live");
        received
            .Should()
            .NotContain(
                m => m.Contains(secret),
                "Bob is not in Alice's room, so no subscription may carry her message to him"
            );
    }

    /// <summary>Every protocol message <paramref name="sub"/> receives within <paramref name="window"/>.</summary>
    private static async Task<List<JsonElement>> DrainAsync(
        GraphQLWebSocketClient sub,
        TimeSpan window
    )
    {
        var messages = new List<JsonElement>();
        var deadline = DateTime.UtcNow + window;
        for (
            var remaining = window;
            remaining > TimeSpan.Zero;
            remaining = deadline - DateTime.UtcNow
        )
        {
            try
            {
                messages.Add(await sub.ReceiveAnyAsync(remaining));
            }
            catch (TimeoutException)
            {
                break;
            }
        }
        return messages;
    }

    [Test]
    public async Task An_anonymous_socket_is_refused_at_connection_init()
    {
        // With a token scheme registered, every socket must carry a credential in connection_init;
        // one without is closed with 4403 before it can subscribe to anything.
        var connect = () => ConnectAsync(apiKey: null);

        var closed = (await connect.Should().ThrowAsync<WebSocketClosedException>()).Which;
        ((int?)closed.Status).Should().Be(4403);
    }
}
