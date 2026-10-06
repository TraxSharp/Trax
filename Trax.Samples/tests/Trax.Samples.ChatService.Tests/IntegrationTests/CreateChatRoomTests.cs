using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.CreateChatRoom;
using Trax.Samples.ChatService.Trains.CreateChatRoom.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class CreateChatRoomTests
{
    [Test]
    public async Task ValidateInput_EmptyName_Throws()
    {
        var act = () => new ValidateInputJunction().Run(new CreateChatRoomInput { Name = "" });

        await act.Should().ThrowAsync<TrainException>().WithMessage("*name*");
    }

    [Test]
    public async Task ValidateInput_ValidInput_ReturnsInput()
    {
        var input = new CreateChatRoomInput { Name = "General" };

        var result = await new ValidateInputJunction().Run(input);

        result.Should().Be(input);
    }

    [Test]
    public async Task PersistRoom_CreatesRoomWithTheCallerAsItsParticipant()
    {
        using var db = ChatDbContextFixture.Create();
        var junction = new PersistRoomJunction(
            db,
            ChatUsers.Alice,
            NullLogger<PersistRoomJunction>.Instance
        );

        var result = await junction.Run(new CreateChatRoomInput { Name = "General" });

        result.ChatRoomId.Should().NotBeEmpty();
        result.Name.Should().Be("General");
        db.ChatRooms.Single(r => r.Id == result.ChatRoomId)
            .CreatedByUserId.Should()
            .Be("TraxApiKey:alice");
        var participant = db.ChatParticipants.Single(p => p.ChatRoomId == result.ChatRoomId);
        participant.UserId.Should().Be("TraxApiKey:alice");
        participant.DisplayName.Should().Be("Alice");
    }
}
