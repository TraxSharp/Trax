using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.GetChatRooms.Junctions;

namespace Trax.Samples.ChatService.Trains.GetChatRooms;

[TraxQuery(Description = "Lists chat rooms the user participates in")]
public class GetChatRoomsTrain
    : ServiceTrain<GetChatRoomsInput, GetChatRoomsOutput>,
        IGetChatRoomsTrain
{
    protected override GetChatRoomsOutput Junctions() => Chain<FetchRoomsJunction>();
}
