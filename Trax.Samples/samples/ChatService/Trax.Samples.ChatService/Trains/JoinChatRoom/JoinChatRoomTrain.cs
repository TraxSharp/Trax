using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom;

[TraxAllowAnonymous]
[TraxMutation(Description = "Adds a user to an existing chat room")]
[TraxBroadcast]
public class JoinChatRoomTrain
    : ServiceTrain<JoinChatRoomInput, JoinChatRoomOutput>,
        IJoinChatRoomTrain
{
    protected override Task<Either<Exception, JoinChatRoomOutput>> Junctions() =>
        Chain<ValidateJoinJunction>().Chain<AddParticipantJunction>().Resolve();
}
