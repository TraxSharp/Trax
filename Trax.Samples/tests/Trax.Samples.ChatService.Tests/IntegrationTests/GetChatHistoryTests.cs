using AwesomeAssertions;
using Trax.Core.Exceptions;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.GetChatHistory;
using Trax.Samples.ChatService.Trains.GetChatHistory.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class GetChatHistoryTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task FetchMessages_ReturnsMessagesInChronologicalOrder()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, 5);

        var result = await Fetch(db, new GetChatHistoryInput { ChatRoomId = roomId });

        result.Messages.Should().HaveCount(5);
        result.Messages.Should().BeInAscendingOrder(m => m.SentAt);
    }

    [Test]
    public async Task FetchMessages_RespectsTake()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, 10);

        var result = await Fetch(db, new GetChatHistoryInput { ChatRoomId = roomId, Take = 3 });

        result.Messages.Should().HaveCount(3);
    }

    [Test]
    public async Task FetchMessages_DefaultsTake_WhenOmitted()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, GetChatHistoryInput.DefaultTake + 5);

        var result = await Fetch(db, new GetChatHistoryInput { ChatRoomId = roomId });

        result.Messages.Should().HaveCount(GetChatHistoryInput.DefaultTake);
    }

    [Test]
    public async Task FetchMessages_ClampsTakeToTheMaximum()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, GetChatHistoryInput.MaxTake + 5);

        var result = await Fetch(
            db,
            new GetChatHistoryInput { ChatRoomId = roomId, Take = 100_000 }
        );

        result.Messages.Should().HaveCount(GetChatHistoryInput.MaxTake);
    }

    [Test]
    public async Task FetchMessages_BeforeFilter_ReturnsOlderMessages()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, 5);

        var result = await Fetch(
            db,
            new GetChatHistoryInput { ChatRoomId = roomId, Before = BaseTime.AddMinutes(3) }
        );

        result.Messages.Should().HaveCount(3);
    }

    [Test]
    public async Task FetchMessages_EmptyRoom_ReturnsEmpty()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, 0);

        var result = await Fetch(db, new GetChatHistoryInput { ChatRoomId = roomId });

        result.Messages.Should().BeEmpty();
    }

    [Test]
    public async Task FetchMessages_DifferentRoom_DoesNotCrossContaminate()
    {
        using var db = ChatDbContextFixture.Create();
        var roomA = await SeedRoomWithMessages(db, 3);
        await SeedRoomWithMessages(db, 4);

        var result = await Fetch(db, new GetChatHistoryInput { ChatRoomId = roomA });

        result.Messages.Should().HaveCount(3);
    }

    [Test]
    public async Task FetchMessages_CallerNotParticipant_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithMessages(db, 2);
        var junction = new FetchMessagesJunction(db, ChatUsers.Bob);

        var act = () => junction.Run(new GetChatHistoryInput { ChatRoomId = roomId });

        await act.Should().ThrowAsync<TrainException>().WithMessage("*not a participant*");
    }

    private static Task<GetChatHistoryOutput> Fetch(ChatDbContext db, GetChatHistoryInput input) =>
        new FetchMessagesJunction(db, ChatUsers.Alice).Run(input);

    private static async Task<Guid> SeedRoomWithMessages(ChatDbContext db, int count)
    {
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        for (var i = 0; i < count; i++)
            db.ChatMessages.Add(
                new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ChatRoomId = roomId,
                    SenderUserId = ChatUsers.Alice.Id,
                    SenderDisplayName = "Alice",
                    Content = $"Message {i}",
                    SentAt = BaseTime.AddMinutes(i),
                }
            );
        await db.SaveChangesAsync();
        return roomId;
    }
}
