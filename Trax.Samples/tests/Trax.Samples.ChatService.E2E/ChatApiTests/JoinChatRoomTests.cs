using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.ChatService.E2E.Fixtures;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

[TestFixture]
public class JoinChatRoomTests : ChatApiTestFixture
{
    private async Task<string> CreateRoom()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Join Test Room" }
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
    public async Task JoinChatRoom_ReturnsOutput()
    {
        var chatRoomId = await CreateRoom();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    joinChatRoom(
                        input: { chatRoomId: "{{chatRoomId}}" }
                    ) {
                        externalId
                        output {
                            chatRoomId
                            userId
                            joinedAt
                        }
                    }
                }
            }
            """,
            apiKey: BobKey
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var output = result.GetData("dispatch", "joinChatRoom").GetProperty("output");
        output.GetProperty("userId").GetString().Should().Be("TraxApiKey:bob");
    }

    [Test]
    public async Task JoinChatRoom_AddsParticipantToDatabase()
    {
        var chatRoomId = await CreateRoom();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    joinChatRoom(
                        input: { chatRoomId: "{{chatRoomId}}" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: BobKey
        );

        result.HasErrors.Should().BeFalse();

        var participant = await ChatDb
            .ChatParticipants.AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.ChatRoomId == Guid.Parse(chatRoomId) && p.UserId == "TraxApiKey:bob"
            );

        participant.Should().NotBeNull();
        participant!.DisplayName.Should().Be("Bob");
    }

    [Test]
    public async Task JoinChatRoom_CreatesTraxMetadata()
    {
        var chatRoomId = await CreateRoom();

        // Clean metadata from CreateChatRoom
        DataContext.Reset();
        await DataContext.Metadatas.ExecuteDeleteAsync();
        DataContext.Reset();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    joinChatRoom(
                        input: { chatRoomId: "{{chatRoomId}}" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: CharlieKey
        );

        result.HasErrors.Should().BeFalse();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            DataContext.Reset();
            var metadata = await DataContext
                .Metadatas.AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.Name.Contains("JoinChatRoom") && m.TrainState == TrainState.Completed
                );

            if (metadata != null)
                return;

            await Task.Delay(250);
        }

        Assert.Fail("JoinChatRoom metadata not found within 5 seconds");
    }
}
