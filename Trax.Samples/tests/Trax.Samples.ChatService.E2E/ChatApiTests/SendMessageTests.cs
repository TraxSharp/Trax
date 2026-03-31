using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.ChatService.E2E.Fixtures;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

[TestFixture]
public class SendMessageTests : ChatApiTestFixture
{
    private async Task<string> CreateRoomWithAlice()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Message Test Room", userId: "alice", displayName: "Alice" }
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
    public async Task SendMessage_ReturnsOutput()
    {
        var chatRoomId = await CreateRoomWithAlice();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    sendMessage(
                        input: { chatRoomId: "{{chatRoomId}}", senderUserId: "alice", content: "Hello E2E!" }
                    ) {
                        externalId
                        output {
                            messageId
                            chatRoomId
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

        var output = result.GetData("dispatch", "sendMessage").GetProperty("output");
        output.GetProperty("content").GetString().Should().Be("Hello E2E!");
        output.GetProperty("messageId").GetString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task SendMessage_PersistsToDatabase()
    {
        var chatRoomId = await CreateRoomWithAlice();

        await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    sendMessage(
                        input: { chatRoomId: "{{chatRoomId}}", senderUserId: "alice", content: "Persisted!" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        var message = await ChatDb
            .ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.ChatRoomId == Guid.Parse(chatRoomId) && m.Content == "Persisted!"
            );

        message.Should().NotBeNull();
        message!.SenderUserId.Should().Be("alice");
    }

    [Test]
    public async Task SendMessage_MultipleMessages_PreservesOrder()
    {
        var chatRoomId = await CreateRoomWithAlice();
        var contents = new[] { "First", "Second", "Third" };

        foreach (var content in contents)
        {
            var result = await GraphQL.SendAsync(
                $$"""
                mutation {
                    dispatch {
                        sendMessage(
                            input: { chatRoomId: "{{chatRoomId}}", senderUserId: "alice", content: "{{content}}" }
                        ) {
                            externalId
                        }
                    }
                }
                """,
                apiKey: AliceKey
            );

            result.HasErrors.Should().BeFalse();
        }

        var messages = await ChatDb
            .ChatMessages.AsNoTracking()
            .Where(m => m.ChatRoomId == Guid.Parse(chatRoomId))
            .OrderBy(m => m.SentAt)
            .Select(m => m.Content)
            .ToListAsync();

        messages.Should().ContainInOrder("First", "Second", "Third");
    }

    [Test]
    public async Task SendMessage_CreatesTraxMetadata()
    {
        var chatRoomId = await CreateRoomWithAlice();

        // Clean metadata from CreateChatRoom
        DataContext.Reset();
        await DataContext.Metadatas.ExecuteDeleteAsync();
        DataContext.Reset();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    sendMessage(
                        input: { chatRoomId: "{{chatRoomId}}", senderUserId: "alice", content: "Meta test" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            DataContext.Reset();
            var metadata = await DataContext
                .Metadatas.AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.Name.Contains("SendMessage") && m.TrainState == TrainState.Completed
                );

            if (metadata != null)
            {
                metadata.Input.Should().Contain("Meta test");
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("SendMessage metadata not found within 5 seconds");
    }
}
