using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom;

// Run only: the junctions act as the request's caller, and a queued run, executed later by a
// scheduler, would have none. Nothing in this host would run it anyway: there is no scheduler.
[TraxAuthorize(Roles = nameof(ChatRole.User))]
[TraxMutation(GraphQLOperation.Run, Description = "Adds the caller to an existing chat room")]
public class JoinChatRoomTrain
    : ServiceTrain<JoinChatRoomInput, JoinChatRoomOutput>,
        IJoinChatRoomTrain
{
    protected override Task<Either<Exception, JoinChatRoomOutput>> Junctions() =>
        Chain<ValidateJoinJunction>().Chain<AddParticipantJunction>().Resolve();
}
