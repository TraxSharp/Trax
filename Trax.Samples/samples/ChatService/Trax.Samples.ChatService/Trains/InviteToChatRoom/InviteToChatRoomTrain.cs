using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Trains.InviteToChatRoom.Junctions;

namespace Trax.Samples.ChatService.Trains.InviteToChatRoom;

// Run only: the junctions act as the request's caller, and a queued run, executed later by a
// scheduler, would have none. Nothing in this host would run it anyway: there is no scheduler.
[TraxAuthorize(Roles = nameof(ChatRole.User))]
[TraxMutation(
    GraphQLOperation.Run,
    Description = "Adds someone the chat knows to a room the caller is in"
)]
public class InviteToChatRoomTrain
    : ServiceTrain<InviteToChatRoomInput, InviteToChatRoomOutput>,
        IInviteToChatRoomTrain
{
    protected override Task<Either<Exception, InviteToChatRoomOutput>> Junctions() =>
        Chain<ValidateInviteJunction>().Chain<AddInviteeJunction>().Resolve();
}
