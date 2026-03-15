using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.GetChatHistory;
using Trax.Samples.ChatService.Trains.GetChatHistory.Steps;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class GetChatHistoryTests
{
    #region FetchMessagesStep

    [Test]
    public async Task FetchMessages_ReturnsMessagesInChronologicalOrder()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, 5);

        var step = new FetchMessagesStep(db, NullLogger<FetchMessagesStep>.Instance);
        var input = new GetChatHistoryInput { ChatRoomId = roomId, Take = 50 };

        var result = await step.Run(input);

        result.Messages.Should().HaveCount(5);
        result.Messages.Should().BeInAscendingOrder(m => m.SentAt);
    }

    [Test]
    public async Task FetchMessages_RespectsPageSize()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, 10);

        var step = new FetchMessagesStep(db, NullLogger<FetchMessagesStep>.Instance);
        var input = new GetChatHistoryInput { ChatRoomId = roomId, Take = 3 };

        var result = await step.Run(input);

        result.Messages.Should().HaveCount(3);
    }

    [Test]
    public async Task FetchMessages_BeforeFilter_ReturnsOlderMessages()
    {
        using var db = ChatDbContextFixture.Create();
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var roomId = await SeedRoomWithTimedMessages(db, baseTime, 5);

        var step = new FetchMessagesStep(db, NullLogger<FetchMessagesStep>.Instance);
        var input = new GetChatHistoryInput
        {
            ChatRoomId = roomId,
            Take = 50,
            Before = baseTime.AddMinutes(3),
        };

        var result = await step.Run(input);

        result.Messages.Should().HaveCount(3);
        result
            .Messages.Should()
            .AllSatisfy(m => m.SentAt.Should().BeBefore(baseTime.AddMinutes(3)));
    }

    [Test]
    public async Task FetchMessages_EmptyRoom_ReturnsEmpty()
    {
        using var db = ChatDbContextFixture.Create();
        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = "Empty",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "alice",
        };
        db.ChatRooms.Add(room);
        await db.SaveChangesAsync();

        var step = new FetchMessagesStep(db, NullLogger<FetchMessagesStep>.Instance);
        var input = new GetChatHistoryInput { ChatRoomId = room.Id, Take = 50 };

        var result = await step.Run(input);

        result.Messages.Should().BeEmpty();
    }

    [Test]
    public async Task FetchMessages_DifferentRoom_DoesNotCrossContaminate()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId1 = await SeedRoomWithMessages(db, 3);
        var roomId2 = await SeedRoomWithMessages(db, 5);

        var step = new FetchMessagesStep(db, NullLogger<FetchMessagesStep>.Instance);
        var input = new GetChatHistoryInput { ChatRoomId = roomId1, Take = 50 };

        var result = await step.Run(input);

        result.Messages.Should().HaveCount(3);
    }

    #endregion

    #region Helpers

    private static async Task<Guid> SeedRoomWithMessages(ChatDbContext db, int count)
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return await SeedRoomWithTimedMessages(db, baseTime, count);
    }

    private static async Task<Guid> SeedRoomWithTimedMessages(
        ChatDbContext db,
        DateTime baseTime,
        int count
    )
    {
        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = "Test Room",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "alice",
        };
        db.ChatRooms.Add(room);

        for (var i = 0; i < count; i++)
        {
            db.ChatMessages.Add(
                new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ChatRoomId = room.Id,
                    SenderUserId = "alice",
                    SenderDisplayName = "Alice",
                    Content = $"Message {i}",
                    SentAt = baseTime.AddMinutes(i),
                }
            );
        }

        await db.SaveChangesAsync();
        return room.Id;
    }

    #endregion
}
