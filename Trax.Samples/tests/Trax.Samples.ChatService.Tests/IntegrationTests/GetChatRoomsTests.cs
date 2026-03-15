using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.GetChatRooms;
using Trax.Samples.ChatService.Trains.GetChatRooms.Steps;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class GetChatRoomsTests
{
    #region FetchRoomsStep

    [Test]
    public async Task FetchRooms_ReturnsOnlyRoomsUserIsIn()
    {
        using var db = ChatDbContextFixture.Create();
        var aliceRoomId = await SeedRoomWithParticipant(db, "alice", "Room A");
        await SeedRoomWithParticipant(db, "bob", "Room B");

        var step = new FetchRoomsStep(db, NullLogger<FetchRoomsStep>.Instance);
        var input = new GetChatRoomsInput { UserId = "alice" };

        var result = await step.Run(input);

        result.Rooms.Should().ContainSingle();
        result.Rooms[0].Id.Should().Be(aliceRoomId);
        result.Rooms[0].Name.Should().Be("Room A");
    }

    [Test]
    public async Task FetchRooms_IncludesParticipantCount()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithParticipant(db, "alice", "Room A");
        db.ChatParticipants.Add(
            new ChatParticipant
            {
                Id = Guid.NewGuid(),
                ChatRoomId = roomId,
                UserId = "bob",
                DisplayName = "Bob",
                JoinedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var step = new FetchRoomsStep(db, NullLogger<FetchRoomsStep>.Instance);
        var input = new GetChatRoomsInput { UserId = "alice" };

        var result = await step.Run(input);

        result.Rooms.Should().ContainSingle();
        result.Rooms[0].ParticipantCount.Should().Be(2);
    }

    [Test]
    public async Task FetchRooms_NoRooms_ReturnsEmpty()
    {
        using var db = ChatDbContextFixture.Create();

        var step = new FetchRoomsStep(db, NullLogger<FetchRoomsStep>.Instance);
        var input = new GetChatRoomsInput { UserId = "nobody" };

        var result = await step.Run(input);

        result.Rooms.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static async Task<Guid> SeedRoomWithParticipant(
        ChatDbContext db,
        string userId,
        string roomName
    )
    {
        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = roomName,
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
