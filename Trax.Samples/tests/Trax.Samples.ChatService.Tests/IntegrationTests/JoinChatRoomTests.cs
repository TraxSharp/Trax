using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.JoinChatRoom;
using Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class JoinChatRoomTests
{
    #region ValidateJoinJunction

    [Test]
    public void ValidateJoin_RoomDoesNotExist_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var junction = new ValidateJoinJunction(db, NullLogger<ValidateJoinJunction>.Instance);
        var input = new JoinChatRoomInput
        {
            ChatRoomId = Guid.NewGuid(),
            UserId = "alice",
            DisplayName = "Alice",
        };

        var act = () => junction.Run(input);

        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
    }

    [Test]
    public async Task ValidateJoin_AlreadyParticipant_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoom(db, "alice");

        db.ChatParticipants.Add(
            new ChatParticipant
            {
                Id = Guid.NewGuid(),
                ChatRoomId = roomId,
                UserId = "alice",
                DisplayName = "Alice",
                JoinedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var junction = new ValidateJoinJunction(db, NullLogger<ValidateJoinJunction>.Instance);
        var input = new JoinChatRoomInput
        {
            ChatRoomId = roomId,
            UserId = "alice",
            DisplayName = "Alice",
        };

        var act = () => junction.Run(input);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*already a participant*");
    }

    [Test]
    public async Task ValidateJoin_ValidNewParticipant_ReturnsInput()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoom(db, "alice");

        var junction = new ValidateJoinJunction(db, NullLogger<ValidateJoinJunction>.Instance);
        var input = new JoinChatRoomInput
        {
            ChatRoomId = roomId,
            UserId = "bob",
            DisplayName = "Bob",
        };

        var result = await junction.Run(input);

        result.Should().Be(input);
    }

    #endregion

    #region AddParticipantJunction

    [Test]
    public async Task AddParticipant_PersistsParticipant()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoom(db, "alice");

        var junction = new AddParticipantJunction(db, NullLogger<AddParticipantJunction>.Instance);
        var input = new JoinChatRoomInput
        {
            ChatRoomId = roomId,
            UserId = "bob",
            DisplayName = "Bob",
        };

        var result = await junction.Run(input);

        result.ChatRoomId.Should().Be(roomId);
        result.UserId.Should().Be("bob");
        result.DisplayName.Should().Be("Bob");
        result.JoinedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        db.ChatParticipants.Should()
            .ContainSingle(p => p.ChatRoomId == roomId && p.UserId == "bob");
    }

    #endregion

    #region Helpers

    private static async Task<Guid> SeedRoom(ChatDbContext db, string creatorId)
    {
        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = "Test Room",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = creatorId,
        };
        db.ChatRooms.Add(room);
        await db.SaveChangesAsync();
        return room.Id;
    }

    #endregion
}
