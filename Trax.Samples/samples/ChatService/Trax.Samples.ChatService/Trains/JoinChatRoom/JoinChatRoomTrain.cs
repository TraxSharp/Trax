using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom;

[TraxMutation(Description = "Adds a user to an existing chat room")]
[TraxBroadcast]
public class JoinChatRoomTrain
    : ServiceTrain<JoinChatRoomInput, JoinChatRoomOutput>,
        IJoinChatRoomTrain
{
    protected override async Task<Either<Exception, JoinChatRoomOutput>> RunInternal(
        JoinChatRoomInput input
    ) => Activate(input).Chain<ValidateJoinJunction>().Chain<AddParticipantJunction>().Resolve();
}
