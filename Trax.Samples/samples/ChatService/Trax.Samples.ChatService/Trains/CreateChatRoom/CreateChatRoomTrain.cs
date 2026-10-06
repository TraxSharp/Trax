using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Trains.CreateChatRoom.Junctions;

namespace Trax.Samples.ChatService.Trains.CreateChatRoom;

// Run only: the junctions act as the request's caller, and a queued run, executed later by a
// scheduler, would have none. Nothing in this host would run it anyway: there is no scheduler.
[TraxAuthorize(Roles = nameof(ChatRole.User))]
[TraxMutation(
    GraphQLOperation.Run,
    Description = "Creates a new chat room and adds the creator as a participant"
)]
public class CreateChatRoomTrain
    : ServiceTrain<CreateChatRoomInput, CreateChatRoomOutput>,
        ICreateChatRoomTrain
{
    protected override Task<Either<Exception, CreateChatRoomOutput>> Junctions() =>
        Chain<ValidateInputJunction>().Chain<PersistRoomJunction>().Resolve();
}
