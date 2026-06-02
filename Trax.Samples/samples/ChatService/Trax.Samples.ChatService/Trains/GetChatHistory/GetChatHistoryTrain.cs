using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.GetChatHistory.Junctions;

namespace Trax.Samples.ChatService.Trains.GetChatHistory;

[TraxAllowAnonymous]
[TraxQuery(Description = "Retrieves message history for a chat room")]
public class GetChatHistoryTrain
    : ServiceTrain<GetChatHistoryInput, GetChatHistoryOutput>,
        IGetChatHistoryTrain
{
    protected override Task<Either<Exception, GetChatHistoryOutput>> Junctions() =>
        Chain<FetchMessagesJunction>().Resolve();
}
