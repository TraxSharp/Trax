using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.MarkChatAsRead.Junctions;

namespace Trax.Samples.ChatService.Trains.MarkChatAsRead;

[TraxMutation(Description = "Marks a chat room as read for a user")]
public class MarkChatAsReadTrain : ServiceTrain<MarkChatAsReadInput, Unit>, IMarkChatAsReadTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(MarkChatAsReadInput input) =>
        Activate(input).Chain<UpdateLastReadJunction>().Resolve();
}
