using Microsoft.EntityFrameworkCore;
using Trax.Samples.ChatService.E2E.Fixtures;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

[TestFixture]
public class ChatQueryTests : ChatApiTestFixture
{
    private async Task<string> CreateRoomAndSendMessages(string roomName, int messageCount)
    {
        var createResult = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "{{roomName}}" }
                    ) {
                        output { chatRoomId }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        createResult.HasErrors.Should().BeFalse();
        var chatRoomId = createResult
            .GetData("dispatch", "createChatRoom", "output")
            .GetProperty("chatRoomId")
            .GetString()!;

        for (var i = 1; i <= messageCount; i++)
        {
            var sendResult = await GraphQL.SendAsync(
                $$"""
                mutation {
                    dispatch {
                        sendMessage(
                            input: { chatRoomId: "{{chatRoomId}}", content: "Message {{i}}" }
                        ) {
                            externalId
                        }
                    }
                }
                """,
                apiKey: AliceKey
            );

            sendResult.HasErrors.Should().BeFalse();
        }

        return chatRoomId;
    }

    [Test]
    public async Task GetChatHistory_ReturnsMessages()
    {
        var chatRoomId = await CreateRoomAndSendMessages("History Room", 3);

        var result = await GraphQL.SendAsync(
            $$"""
            {
                discover {
                    getChatHistory(input: { chatRoomId: "{{chatRoomId}}", take: 100 }) {
                        messages {
                            senderDisplayName
                            content
                            sentAt
                        }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var messages = result.GetData("discover", "getChatHistory").GetProperty("messages");
        messages.GetArrayLength().Should().Be(3);
    }

    [Test]
    public async Task GetChatRooms_ForUser_ReturnsJoinedRooms()
    {
        await CreateRoomAndSendMessages("Room A", 0);
        await CreateRoomAndSendMessages("Room B", 0);

        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    getChatRooms {
                        rooms {
                            name
                        }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var rooms = result.GetData("discover", "getChatRooms").GetProperty("rooms");
        rooms.GetArrayLength().Should().Be(2);
    }

    [Test]
    public async Task GetChatRooms_ExcludesNonJoinedRooms()
    {
        // Create room as Alice — Bob is NOT a participant.
        await CreateRoomAndSendMessages("Alice Only", 0);

        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    getChatRooms {
                        rooms {
                            name
                        }
                    }
                }
            }
            """,
            apiKey: BobKey
        );

        result.HasErrors.Should().BeFalse();

        var rooms = result.GetData("discover", "getChatRooms").GetProperty("rooms");
        rooms.GetArrayLength().Should().Be(0, "Bob hasn't joined any rooms");
    }

    [Test]
    public async Task GetChatHistory_ClampsTakeToTheMaximum()
    {
        var chatRoomId = await CreateRoomAndSendMessages("Busy Room", 3);

        var result = await GraphQL.SendAsync(
            $$"""
            {
                discover {
                    getChatHistory(input: { chatRoomId: "{{chatRoomId}}", take: 100000 }) {
                        messages { content }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        // A huge take is clamped, not refused: the caller gets the room's three messages.
        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        result
            .GetData("discover", "getChatHistory")
            .GetProperty("messages")
            .GetArrayLength()
            .Should()
            .Be(3);
    }

    [Test]
    public async Task GetChatHistory_EmptyRoom_ReturnsEmptyList()
    {
        var createResult = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Empty Room" }
                    ) {
                        output { chatRoomId }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        createResult.HasErrors.Should().BeFalse();
        var chatRoomId = createResult
            .GetData("dispatch", "createChatRoom", "output")
            .GetProperty("chatRoomId")
            .GetString()!;

        var result = await GraphQL.SendAsync(
            $$"""
            {
                discover {
                    getChatHistory(input: { chatRoomId: "{{chatRoomId}}", take: 100 }) {
                        messages {
                            content
                        }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        var messages = result.GetData("discover", "getChatHistory").GetProperty("messages");
        messages.GetArrayLength().Should().Be(0);
    }
}
