using Trax.Samples.ChatService.E2E.Fixtures;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

/// <summary>
/// The acting user of a chat operation is the authenticated caller, never a user id the caller
/// writes into the input. Every operation needs a credential, and a room's history and messages
/// are for its participants only.
/// </summary>
[TestFixture]
public class ChatCallerIdentityTests : ChatApiTestFixture
{
    private async Task<string> AliceRoomWithOneMessage()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "Private");
        var sent = await SendMessageAsAsync(AliceKey, roomId, "alice only");
        sent.HasErrors.Should().BeFalse(sent.FirstErrorMessage);
        return roomId;
    }

    private static string HistoryQuery(string roomId) =>
        $$"""
            {
                discover {
                    getChatHistory(input: { chatRoomId: "{{roomId}}", take: 100 }) {
                        messages { senderUserId content }
                    }
                }
            }
            """;

    private static bool ReturnedMessages(Utilities.GraphQLResponse response) =>
        response.Root.TryGetProperty("data", out var data)
        && data.ValueKind == System.Text.Json.JsonValueKind.Object
        && data.TryGetProperty("discover", out var discover)
        && discover.ValueKind == System.Text.Json.JsonValueKind.Object
        && discover.TryGetProperty("getChatHistory", out var history)
        && history.ValueKind == System.Text.Json.JsonValueKind.Object
        && history.GetProperty("messages").GetArrayLength() > 0;

    [Test]
    public async Task Anonymous_caller_cannot_read_a_rooms_history()
    {
        var roomId = await AliceRoomWithOneMessage();

        var response = await GraphQL.SendAsync(HistoryQuery(roomId));

        ReturnedMessages(response)
            .Should()
            .BeFalse("a caller with no credential must not read Alice's room");
        response.FirstErrorMessage.Should().Be("Not authorized.");
    }

    [Test]
    public async Task Non_participant_cannot_read_a_rooms_history()
    {
        var roomId = await AliceRoomWithOneMessage();

        var response = await GraphQL.SendAsync(HistoryQuery(roomId), apiKey: CharlieKey);

        ReturnedMessages(response).Should().BeFalse("Charlie never joined Alice's room");
        response.FirstErrorMessage.Should().Contain("not a participant");
    }

    [Test]
    public async Task Caller_cannot_send_a_message_as_another_user()
    {
        var roomId = await AliceRoomWithOneMessage();

        // The input has no sender field at all: naming one is a GraphQL validation error.
        var response = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    sendMessage(input: { chatRoomId: "{{roomId}}", senderUserId: "alice", content: "forged" }) {
                        output { messageId }
                    }
                }
            }
            """,
            apiKey: BobKey
        );

        response
            .HasErrors.Should()
            .BeTrue("Bob's key must not post a message whose sender is Alice");
    }

    [Test]
    public async Task A_message_is_stored_as_the_caller_who_sent_it()
    {
        var roomId = await CreateRoomAsAsync(AliceKey);
        await JoinRoomAsAsync(BobKey, roomId);

        var sent = await SendMessageAsAsync(BobKey, roomId, "from bob");

        sent.HasErrors.Should().BeFalse(sent.FirstErrorMessage);
        sent.GetData("dispatch", "sendMessage", "output")
            .GetProperty("senderUserId")
            .GetString()
            .Should()
            .Be("TraxApiKey:bob");
    }

    [Test]
    public async Task Non_participant_cannot_send_to_a_room()
    {
        var roomId = await AliceRoomWithOneMessage();

        var sent = await SendMessageAsAsync(CharlieKey, roomId, "intruding");

        sent.HasErrors.Should().BeTrue("Charlie is not a participant in Alice's room");
        sent.FirstErrorMessage.Should().Contain("not a participant");
    }

    [Test]
    public async Task Anonymous_caller_cannot_create_a_room()
    {
        var response = await GraphQL.SendAsync(
            """
            mutation { dispatch { createChatRoom(input: { name: "Nobody's" }) { output { chatRoomId } } } }
            """
        );

        response.FirstErrorMessage.Should().Be("Not authorized.");
    }

    [Test]
    public async Task Rooms_list_only_the_callers_own_rooms()
    {
        await CreateRoomAsAsync(AliceKey, "Alice's");
        var shared = await CreateRoomAsAsync(BobKey, "Bob's");
        await JoinRoomAsAsync(CharlieKey, shared);

        var response = await GraphQL.SendAsync(
            "{ discover { getChatRooms { rooms { name } } } }",
            apiKey: CharlieKey
        );

        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        response
            .GetData("discover", "getChatRooms")
            .GetProperty("rooms")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .Should()
            .Equal("Bob's");
    }
}
