using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.ChatService.E2E.Fixtures;

namespace Trax.Samples.ChatService.E2E.E2ETests;

[TestFixture]
public class ChatServiceE2ETests : ChatApiTestFixture
{
    #region CreateChatRoom

    [Test]
    public async Task CreateChatRoom_ValidInput_ReturnsRoomIdAndPersistsToChatDb()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Persistence Test", userId: "alice", displayName: "Alice" }
                    ) {
                        externalId
                        output {
                            chatRoomId
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

        var dispatch = result.GetData("dispatch", "createChatRoom");
        dispatch.GetProperty("externalId").GetString().Should().NotBeNullOrEmpty();

        var output = dispatch.GetProperty("output");
        var chatRoomIdStr = output.GetProperty("chatRoomId").GetString();
        chatRoomIdStr.Should().NotBeNullOrEmpty();
        output.GetProperty("name").GetString().Should().Be("Persistence Test");

        // Verify persisted to SQLite chat database
        var chatRoomId = Guid.Parse(chatRoomIdStr!);
        var room = await ChatDb
            .ChatRooms.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == chatRoomId);

        room.Should().NotBeNull();
        room!.Name.Should().Be("Persistence Test");
        room.CreatedByUserId.Should().Be("alice");

        // Verify Trax metadata persisted to SQLite with Completed state
        DataContext.Reset();
        var metadata = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.Name.Contains("CreateChatRoom") && m.TrainState == TrainState.Completed
            );

        metadata.Should().NotBeNull("Trax metadata should be persisted to SQLite");
    }

    [Test]
    public async Task CreateChatRoom_AlsoAddsCreatorAsParticipant()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "Participant Test", userId: "alice", displayName: "Alice" }
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

    #endregion

    #region JoinChatRoom

    [Test]
    public async Task JoinChatRoom_ValidRoom_ReturnsJoinedAtAndPersists()
    {
        var chatRoomId = await CreateRoom();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    joinChatRoom(
                        input: { chatRoomId: "{{chatRoomId}}", userId: "bob", displayName: "Bob" }
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
        output.GetProperty("userId").GetString().Should().Be("bob");
        output.GetProperty("joinedAt").GetString().Should().NotBeNullOrEmpty();

        // Verify participant persisted to SQLite chat database
        var participant = await ChatDb
            .ChatParticipants.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ChatRoomId == Guid.Parse(chatRoomId) && p.UserId == "bob");

        participant.Should().NotBeNull();
        participant!.DisplayName.Should().Be("Bob");

        // Verify Trax metadata Completed
        DataContext.Reset();
        var metadata = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.Name.Contains("JoinChatRoom") && m.TrainState == TrainState.Completed
            );

        metadata.Should().NotBeNull();
    }

    #endregion

    #region SendMessage

    [Test]
    public async Task SendMessage_ValidInput_PersistsMessageToChatDb()
    {
        var chatRoomId = await CreateRoom();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    sendMessage(
                        input: { chatRoomId: "{{chatRoomId}}", senderUserId: "alice", content: "Hello from E2E!" }
                    ) {
                        externalId
                        output {
                            messageId
                            content
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
        output.GetProperty("content").GetString().Should().Be("Hello from E2E!");

        // Verify message persisted to SQLite chat database
        var message = await ChatDb
            .ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.ChatRoomId == Guid.Parse(chatRoomId) && m.Content == "Hello from E2E!"
            );

        message.Should().NotBeNull();
        message!.SenderUserId.Should().Be("alice");
    }

    [Test]
    public async Task SendMessage_MetadataPersisted_WithCompletedState()
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
                    sendMessage(
                        input: { chatRoomId: "{{chatRoomId}}", senderUserId: "alice", content: "Metadata check" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse();

        DataContext.Reset();
        var metadata = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.Name.Contains("SendMessage") && m.TrainState == TrainState.Completed
            );

        metadata.Should().NotBeNull("SendMessage metadata should be persisted to SQLite");
        metadata!.TrainState.Should().Be(TrainState.Completed);
        metadata.Name.Should().Contain("SendMessage");
        metadata.Input.Should().Contain("Metadata check");
    }

    #endregion

    #region GetChatHistory

    [Test]
    public async Task GetChatHistory_AfterSendingMessages_ReturnsAll()
    {
        var chatRoomId = await CreateRoom();

        var messages = new[] { "First message", "Second message" };
        foreach (var content in messages)
        {
            var sendResult = await GraphQL.SendAsync(
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

            sendResult.HasErrors.Should().BeFalse();
        }

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

        var returnedMessages = result.GetData("discover", "getChatHistory").GetProperty("messages");
        returnedMessages.GetArrayLength().Should().Be(2);

        returnedMessages[0].GetProperty("content").GetString().Should().Be("First message");
        returnedMessages[1].GetProperty("content").GetString().Should().Be("Second message");
    }

    #endregion

    #region TraxMetadata

    [Test]
    public async Task AllOperations_PersistTraxMetadata_ToSqlite()
    {
        // Create room
        var chatRoomId = await CreateRoom();

        // Join as Bob
        var joinResult = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    joinChatRoom(
                        input: { chatRoomId: "{{chatRoomId}}", userId: "bob", displayName: "Bob" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: BobKey
        );

        joinResult.HasErrors.Should().BeFalse();

        // Send message
        var sendResult = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    sendMessage(
                        input: { chatRoomId: "{{chatRoomId}}", senderUserId: "alice", content: "All ops test" }
                    ) {
                        externalId
                    }
                }
            }
            """,
            apiKey: AliceKey
        );

        sendResult.HasErrors.Should().BeFalse();

        // Verify all 3 metadata records exist (CreateChatRoom, JoinChatRoom, SendMessage)
        DataContext.Reset();
        var allMetadata = await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.Completed)
            .ToListAsync();

        allMetadata.Should().HaveCount(3);
        allMetadata.Should().Contain(m => m.Name.Contains("CreateChatRoom"));
        allMetadata.Should().Contain(m => m.Name.Contains("JoinChatRoom"));
        allMetadata.Should().Contain(m => m.Name.Contains("SendMessage"));
    }

    [Test]
    public async Task FailedTrain_PersistsFailureMetadata()
    {
        var nonExistentRoomId = Guid.NewGuid();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    joinChatRoom(
                        input: { chatRoomId: "{{nonExistentRoomId}}", userId: "bob", displayName: "Bob" }
                    ) {
                        externalId
                        output {
                            userId
                        }
                    }
                }
            }
            """,
            apiKey: BobKey
        );

        // The mutation may return errors or succeed with failed metadata depending on error handling.
        // Either way, check for failed metadata in the Trax SQLite database.
        DataContext.Reset();

        // Poll briefly in case metadata persistence is slightly delayed
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            DataContext.Reset();
            var metadata = await DataContext
                .Metadatas.AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.Name.Contains("JoinChatRoom") && m.TrainState == TrainState.Failed
                );

            if (metadata != null)
            {
                metadata.TrainState.Should().Be(TrainState.Failed);
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("Failed JoinChatRoom metadata not found within 5 seconds");
    }

    #endregion

    #region Helpers

    private async Task<string> CreateRoom(string name = "E2E Test Room")
    {
        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    createChatRoom(
                        input: { name: "{{name}}", userId: "alice", displayName: "Alice" }
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

    #endregion
}
