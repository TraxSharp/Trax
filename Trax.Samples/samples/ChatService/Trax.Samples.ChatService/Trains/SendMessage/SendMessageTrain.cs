using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.SendMessage.Steps;

namespace Trax.Samples.ChatService.Trains.SendMessage;

[TraxMutation(Description = "Sends a message to a chat room")]
[TraxBroadcast]
public class SendMessageTrain : ServiceTrain<SendMessageInput, SendMessageOutput>, ISendMessageTrain
{
    protected override async Task<Either<Exception, SendMessageOutput>> RunInternal(
        SendMessageInput input
    ) => Activate(input).Chain<ValidateSenderStep>().Chain<PersistMessageStep>().Resolve();
}
