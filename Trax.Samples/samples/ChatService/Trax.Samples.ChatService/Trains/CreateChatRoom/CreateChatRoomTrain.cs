using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.CreateChatRoom.Junctions;

namespace Trax.Samples.ChatService.Trains.CreateChatRoom;

[TraxMutation(Description = "Creates a new chat room and adds the creator as a participant")]
[TraxBroadcast]
public class CreateChatRoomTrain
    : ServiceTrain<CreateChatRoomInput, CreateChatRoomOutput>,
        ICreateChatRoomTrain
{
    protected override CreateChatRoomOutput Junctions() =>
        Chain<ValidateInputJunction>().Chain<PersistRoomJunction>();
}
