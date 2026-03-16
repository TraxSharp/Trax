using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Samples.ChatService.Data.Entities;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.CreateChatRoom;
using Trax.Samples.ChatService.Trains.CreateChatRoom.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class CreateChatRoomTests
{
    #region ValidateInputJunction

    [Test]
    public void ValidateInput_EmptyName_Throws()
    {
        var junction = new ValidateInputJunction(NullLogger<ValidateInputJunction>.Instance);
        var input = new CreateChatRoomInput
        {
            Name = "",
            UserId = "alice",
            DisplayName = "Alice",
        };

        var act = () => junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*name*");
    }

    [Test]
    public void ValidateInput_EmptyUserId_Throws()
    {
        var junction = new ValidateInputJunction(NullLogger<ValidateInputJunction>.Instance);
        var input = new CreateChatRoomInput
        {
            Name = "General",
            UserId = "",
            DisplayName = "Alice",
        };

        var act = () => junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*User ID*");
    }

    [Test]
    public async Task ValidateInput_ValidInput_ReturnsInput()
    {
        var junction = new ValidateInputJunction(NullLogger<ValidateInputJunction>.Instance);
        var input = new CreateChatRoomInput
        {
            Name = "General",
            UserId = "alice",
            DisplayName = "Alice",
        };

        var result = await junction.Run(input);

        result.Should().Be(input);
    }

    #endregion

    #region PersistRoomJunction

    [Test]
    public async Task PersistRoom_CreatesRoomAndParticipant()
    {
        using var db = ChatDbContextFixture.Create();
        var junction = new PersistRoomJunction(db, NullLogger<PersistRoomJunction>.Instance);
        var input = new CreateChatRoomInput
        {
            Name = "General",
            UserId = "alice",
            DisplayName = "Alice",
        };

        var result = await junction.Run(input);

        result.Name.Should().Be("General");
        result.ChatRoomId.Should().NotBeEmpty();

        var room = await db.ChatRooms.FindAsync(result.ChatRoomId);
        room.Should().NotBeNull();
        room!.CreatedByUserId.Should().Be("alice");

        db.ChatParticipants.Should()
            .ContainSingle(p => p.ChatRoomId == result.ChatRoomId && p.UserId == "alice");
    }

    #endregion
}
