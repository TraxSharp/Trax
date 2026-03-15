using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.GetChatHistory.Steps;

namespace Trax.Samples.ChatService.Trains.GetChatHistory;

[TraxQuery(Description = "Retrieves message history for a chat room")]
public class GetChatHistoryTrain
    : ServiceTrain<GetChatHistoryInput, GetChatHistoryOutput>,
        IGetChatHistoryTrain
{
    protected override async Task<Either<Exception, GetChatHistoryOutput>> RunInternal(
        GetChatHistoryInput input
    ) => Activate(input).Chain<FetchMessagesStep>().Resolve();
}
