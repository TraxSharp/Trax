using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
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
                        input: { name: "General" }
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
                        input: { name: "Dev Chat" }
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
        var chatRoomId = Guid.Parse(chatRoomIdStr.NotNull());

        var room = await ChatDb
            .ChatRooms.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == chatRoomId);
        room.Should().NotBeNull();
        room.NotNull().Name.Should().Be("Dev Chat");
        room.CreatedByUserId.Should().Be("TraxApiKey:alice");
    }

    [Test]
    public async Task CreateChatRoom_AddsCreatorAsParticipant()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Team" }
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
        var chatRoomId = Guid.Parse(chatRoomIdStr.NotNull());

        var participant = await ChatDb
            .ChatParticipants.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ChatRoomId == chatRoomId && p.UserId == "TraxApiKey:alice");

        participant.Should().NotBeNull();
        participant.NotNull().DisplayName.Should().Be("Alice");
    }

    [Test]
    public async Task CreateChatRoom_CreatesTraxMetadata()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Metadata Test" }
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
        var deadline = TimeProvider.System.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(5);
        while (TimeProvider.System.GetUtcNow().UtcDateTime < deadline)
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
