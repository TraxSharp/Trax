using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.ChatService.E2E.Fixtures;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

[TestFixture]
public class CreateChatRoomTests : ChatApiTestFixture
{
    [Test]
    public async Task CreateChatRoom_ReturnsOutput()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "General", userId: "alice", displayName: "Alice" }
                    ) {
                        externalId
                        output {
                            chatRoomId
                            name
                            createdAt
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

        var dispatch = result.GetData("dispatch", "createChatRoom");
        dispatch.GetProperty("externalId").GetString().Should().NotBeNullOrEmpty();

        var output = dispatch.GetProperty("output");
        output.GetProperty("chatRoomId").GetString().Should().NotBeNullOrEmpty();
        output.GetProperty("name").GetString().Should().Be("General");
    }

    [Test]
    public async Task CreateChatRoom_PersistsToDatabase()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Dev Chat", userId: "alice", displayName: "Alice" }
                    ) {
                        output { chatRoomId }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        var chatRoomIdStr = result
            .GetData("dispatch", "createChatRoom", "output")
            .GetProperty("chatRoomId")
            .GetString();
        var chatRoomId = Guid.Parse(chatRoomIdStr!);

        var room = await ChatDb
            .ChatRooms.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == chatRoomId);
        room.Should().NotBeNull();
        room!.Name.Should().Be("Dev Chat");
        room.CreatedByUserId.Should().Be("alice");
    }

    [Test]
    public async Task CreateChatRoom_AddsCreatorAsParticipant()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Team", userId: "alice", displayName: "Alice" }
                    ) {
                        output { chatRoomId }
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        var chatRoomIdStr = result
            .GetData("dispatch", "createChatRoom", "output")
            .GetProperty("chatRoomId")
            .GetString();
        var chatRoomId = Guid.Parse(chatRoomIdStr!);

        var participant = await ChatDb
            .ChatParticipants.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ChatRoomId == chatRoomId && p.UserId == "alice");

        participant.Should().NotBeNull();
        participant!.DisplayName.Should().Be("Alice");
    }

    [Test]
    public async Task CreateChatRoom_CreatesTraxMetadata()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Metadata Test", userId: "alice", displayName: "Alice" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        // Poll for metadata to be persisted.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            DataContext.Reset();
            var metadata = await DataContext
                .Metadatas.AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.Name.Contains("CreateChatRoom") && m.TrainState == TrainState.Completed
                );

            if (metadata != null)
            {
                metadata.Input.Should().Contain("Metadata Test");
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("CreateChatRoom metadata not found within 5 seconds");
    }
}
