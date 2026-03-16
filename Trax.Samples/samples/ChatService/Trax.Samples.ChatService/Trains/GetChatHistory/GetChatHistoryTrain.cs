using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.GetChatHistory.Junctions;

namespace Trax.Samples.ChatService.Trains.GetChatHistory;

[TraxQuery(Description = "Retrieves message history for a chat room")]
public class GetChatHistoryTrain
    : ServiceTrain<GetChatHistoryInput, GetChatHistoryOutput>,
        IGetChatHistoryTrain
{
    protected override async Task<Either<Exception, GetChatHistoryOutput>> RunInternal(
        GetChatHistoryInput input
    ) => Activate(input).Chain<FetchMessagesJunction>().Resolve();
}
