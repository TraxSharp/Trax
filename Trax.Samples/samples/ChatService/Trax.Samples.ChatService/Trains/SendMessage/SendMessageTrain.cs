using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Trains.SendMessage.Junctions;

namespace Trax.Samples.ChatService.Trains.SendMessage;

// Run only: the junctions act as the request's caller, and a queued run, executed later by a
// scheduler, would have none. Nothing in this host would run it anyway: there is no scheduler.
[TraxAuthorize(Roles = nameof(ChatRole.User))]
[TraxMutation(GraphQLOperation.Run, Description = "Sends a message to a chat room")]
public class SendMessageTrain : ServiceTrain<SendMessageInput, SendMessageOutput>, ISendMessageTrain
{
    protected override Task<Either<Exception, SendMessageOutput>> Junctions() =>
        Chain<ValidateSenderJunction>().Chain<PersistMessageJunction>().Resolve();
}
