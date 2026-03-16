using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.MarkChatAsRead;
using Trax.Samples.ChatService.Trains.MarkChatAsRead.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class MarkChatAsReadTests
{
    #region UpdateLastReadJunction

    [Test]
    public async Task UpdateLastRead_SetsLastReadAt()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithParticipant(db, "alice");

        var junction = new UpdateLastReadJunction(db, NullLogger<UpdateLastReadJunction>.Instance);
        var input = new MarkChatAsReadInput { ChatRoomId = roomId, UserId = "alice" };

        await junction.Run(input);

        var participant = db.ChatParticipants.First(p =>
            p.ChatRoomId == roomId && p.UserId == "alice"
        );
        participant.LastReadAt.Should().NotBeNull();
        participant.LastReadAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public void UpdateLastRead_UserNotParticipant_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var junction = new UpdateLastReadJunction(db, NullLogger<UpdateLastReadJunction>.Instance);
        var input = new MarkChatAsReadInput { ChatRoomId = Guid.NewGuid(), UserId = "nobody" };

        var act = () => junction.Run(input);

        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a participant*");
    }

    #endregion

    #region Helpers

    private static async Task<Guid> SeedRoomWithParticipant(ChatDbContext db, string userId)
    {
        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
        };
        db.ChatRooms.Add(room);

        db.ChatParticipants.Add(
            new ChatParticipant
            {
                Id = Guid.NewGuid(),
                ChatRoomId = room.Id,
                UserId = userId,
                DisplayName = userId,
                JoinedAt = DateTime.UtcNow,
            }
        );

        await db.SaveChangesAsync();
        return room.Id;
    }

    #endregion
}
