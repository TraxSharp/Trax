using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Trains.GetChatRooms.Junctions;

namespace Trax.Samples.ChatService.Trains.GetChatRooms;

[TraxAuthorize(Roles = nameof(ChatRole.User))]
[TraxQuery(Description = "Lists the chat rooms the caller participates in")]
public class GetChatRoomsTrain
    : ServiceTrain<GetChatRoomsInput, GetChatRoomsOutput>,
        IGetChatRoomsTrain
{
    protected override Task<Either<Exception, GetChatRoomsOutput>> Junctions() =>
        Chain<FetchRoomsJunction>().Resolve();
}
