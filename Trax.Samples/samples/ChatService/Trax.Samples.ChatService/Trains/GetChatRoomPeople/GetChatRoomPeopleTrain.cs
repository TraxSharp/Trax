using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Trains.GetChatRoomPeople.Junctions;

namespace Trax.Samples.ChatService.Trains.GetChatRoomPeople;

[TraxAuthorize(Roles = nameof(ChatRole.User))]
[TraxQuery(
    Description = "Lists who a member of a room could invite to it, and who is in it already"
)]
public class GetChatRoomPeopleTrain
    : ServiceTrain<GetChatRoomPeopleInput, GetChatRoomPeopleOutput>,
        IGetChatRoomPeopleTrain
{
    protected override Task<Either<Exception, GetChatRoomPeopleOutput>> Junctions() =>
        Chain<FetchRoomPeopleJunction>().Resolve();
}
