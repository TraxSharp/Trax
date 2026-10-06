using AwesomeAssertions;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.GetChatRooms;
using Trax.Samples.ChatService.Trains.GetChatRooms.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class GetChatRoomsTests
{
    [Test]
    public async Task FetchRooms_ReturnsOnlyTheCallersRooms()
    {
        using var db = ChatDbContextFixture.Create();
        var mine = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        await ChatUsers.SeedRoomAsync(db, ChatUsers.Bob);

        var result = await new FetchRoomsJunction(db, ChatUsers.Alice).Run(new GetChatRoomsInput());

        result.Rooms.Select(r => r.Id).Should().Equal(mine);
    }

    [Test]
    public async Task FetchRooms_IncludesParticipantCount()
    {
        using var db = ChatDbContextFixture.Create();
        await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice, ChatUsers.Bob);

        var result = await new FetchRoomsJunction(db, ChatUsers.Alice).Run(new GetChatRoomsInput());

        result.Rooms.Should().ContainSingle().Which.ParticipantCount.Should().Be(2);
    }

    [Test]
    public async Task FetchRooms_NoRooms_ReturnsEmpty()
    {
        using var db = ChatDbContextFixture.Create();

        var result = await new FetchRoomsJunction(db, ChatUsers.Alice).Run(new GetChatRoomsInput());

        result.Rooms.Should().BeEmpty();
    }
}
