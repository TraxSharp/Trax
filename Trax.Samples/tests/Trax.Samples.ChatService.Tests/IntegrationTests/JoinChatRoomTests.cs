using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.JoinChatRoom;
using Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class JoinChatRoomTests
{
    [Test]
    public async Task ValidateJoin_RoomDoesNotExist_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var junction = new ValidateJoinJunction(db, ChatUsers.Bob);

        var act = () => junction.Run(new JoinChatRoomInput { ChatRoomId = Guid.NewGuid() });

        await act.Should().ThrowAsync<TrainException>().WithMessage("*does not exist*");
    }

    [Test]
    public async Task ValidateJoin_CallerAlreadyParticipant_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new ValidateJoinJunction(db, ChatUsers.Alice);

        var act = () => junction.Run(new JoinChatRoomInput { ChatRoomId = roomId });

        await act.Should().ThrowAsync<TrainException>().WithMessage("*already a participant*");
    }

    [Test]
    public async Task ValidateJoin_NewParticipant_ReturnsInput()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var input = new JoinChatRoomInput { ChatRoomId = roomId };

        var result = await new ValidateJoinJunction(db, ChatUsers.Bob).Run(input);

        result.Should().Be(input);
    }

    [Test]
    public async Task AddParticipant_AddsTheCaller()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new AddParticipantJunction(
            db,
            ChatUsers.Bob,
            NullLogger<AddParticipantJunction>.Instance
        );

        var result = await junction.Run(new JoinChatRoomInput { ChatRoomId = roomId });

        result.UserId.Should().Be("TraxApiKey:bob");
        result.DisplayName.Should().Be("Bob");
        db.ChatParticipants.Should()
            .ContainSingle(p => p.ChatRoomId == roomId && p.UserId == "TraxApiKey:bob");
    }
}
