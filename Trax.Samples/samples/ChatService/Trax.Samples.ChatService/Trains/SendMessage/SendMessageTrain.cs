using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.SendMessage.Junctions;

namespace Trax.Samples.ChatService.Trains.SendMessage;

[TraxAllowAnonymous]
[TraxMutation(Description = "Sends a message to a chat room")]
[TraxBroadcast]
public class SendMessageTrain : ServiceTrain<SendMessageInput, SendMessageOutput>, ISendMessageTrain
{
    protected override Task<Either<Exception, SendMessageOutput>> Junctions() =>
        Chain<ValidateSenderJunction>().Chain<PersistMessageJunction>().Resolve();
}
