using AwesomeAssertions;
using Trax.Core.Exceptions;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.GetChatRoomPeople;
using Trax.Samples.ChatService.Trains.GetChatRoomPeople.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class GetChatRoomPeopleTests
{
    [Test]
    public async Task FetchRoomPeople_ForANonMember_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new FetchRoomPeopleJunction(db, ChatUsers.Charlie, ChatUsers.Directory());

        var act = () => junction.Run(new GetChatRoomPeopleInput { ChatRoomId = roomId });

        await act.Should().ThrowAsync<TrainException>().WithMessage("*not a participant*");
    }

    [Test]
    public async Task FetchRoomPeople_ListsEveryoneElse_MarkingWhoIsInTheRoom()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice, ChatUsers.Bob);
        var junction = new FetchRoomPeopleJunction(db, ChatUsers.Alice, ChatUsers.Directory());

        var output = await junction.Run(new GetChatRoomPeopleInput { ChatRoomId = roomId });

        output.People.Should().HaveCount(2);
        output.People.Should().NotContain(p => p.UserId == ChatUsers.Alice.Id);
        output.People.Single(p => p.UserId == ChatUsers.Bob.Id).IsMember.Should().BeTrue();
        output.People.Single(p => p.UserId == ChatUsers.Charlie.Id).IsMember.Should().BeFalse();
    }
}
