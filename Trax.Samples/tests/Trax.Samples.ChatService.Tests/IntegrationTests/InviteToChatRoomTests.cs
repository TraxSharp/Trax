using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Samples.ChatService.People;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.InviteToChatRoom;
using Trax.Samples.ChatService.Trains.InviteToChatRoom.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class InviteToChatRoomTests
{
    private static InviteToChatRoomInput Invite(Guid roomId, string userId) =>
        new() { ChatRoomId = roomId, UserId = userId };

    [Test]
    public async Task ValidateInvite_RoomDoesNotExist_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var junction = new ValidateInviteJunction(db, ChatUsers.Alice, ChatUsers.Directory());

        var act = () => junction.Run(Invite(Guid.NewGuid(), ChatUsers.Bob.Id));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*does not exist*");
    }

    [Test]
    public async Task ValidateInvite_CallerNotAMember_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new ValidateInviteJunction(db, ChatUsers.Charlie, ChatUsers.Directory());

        var act = () => junction.Run(Invite(roomId, ChatUsers.Bob.Id));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*not a participant*");
    }

    [Test]
    public async Task ValidateInvite_SomeoneTheDirectoryDoesNotKnow_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new ValidateInviteJunction(db, ChatUsers.Alice, ChatUsers.Directory());

        var act = () => junction.Run(Invite(roomId, "TraxApiKey:mallory"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*no one called*");
    }

    [Test]
    public async Task ValidateInvite_SomeoneAlreadyInTheRoom_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice, ChatUsers.Bob);
        var junction = new ValidateInviteJunction(db, ChatUsers.Alice, ChatUsers.Directory());

        var act = () => junction.Run(Invite(roomId, ChatUsers.Bob.Id));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*already a participant*");
    }

    [Test]
    public async Task ValidateInvite_TheCallerThemselves_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new ValidateInviteJunction(db, ChatUsers.Alice, ChatUsers.Directory());

        var act = () => junction.Run(Invite(roomId, ChatUsers.Alice.Id));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*already in this room*");
    }

    [Test]
    public async Task ValidateInvite_AMemberInvitingSomeoneNew_ReturnsInput()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var input = Invite(roomId, ChatUsers.Bob.Id);

        var result = await new ValidateInviteJunction(
            db,
            ChatUsers.Alice,
            ChatUsers.Directory()
        ).Run(input);

        result.Should().Be(input);
    }

    [Test]
    public async Task AddInvitee_AddsThemUnderTheNameTheDirectoryHolds()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        // The directory, not the inviter, decides what the room calls the invitee.
        var directory = new ChatDirectory([new ChatPerson(ChatUsers.Bob.Id, "Robert")]);
        var junction = new AddInviteeJunction(
            db,
            ChatUsers.Alice,
            directory,
            NullLogger<AddInviteeJunction>.Instance
        );

        var output = await junction.Run(Invite(roomId, ChatUsers.Bob.Id));

        output.DisplayName.Should().Be("Robert");
        output.InvitedByDisplayName.Should().Be("Alice");
        var participant = await db
            .ChatParticipants.AsNoTracking()
            .SingleAsync(p => p.ChatRoomId == roomId && p.UserId == ChatUsers.Bob.Id);
        participant.DisplayName.Should().Be("Robert");
    }
}
