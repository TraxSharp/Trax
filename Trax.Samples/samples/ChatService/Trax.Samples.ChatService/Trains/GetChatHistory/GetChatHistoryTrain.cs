using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Trains.GetChatHistory.Junctions;

namespace Trax.Samples.ChatService.Trains.GetChatHistory;

[TraxAuthorize(Roles = nameof(ChatRole.User))]
[TraxQuery(Description = "Retrieves message history for a chat room the caller participates in")]
public class GetChatHistoryTrain
    : ServiceTrain<GetChatHistoryInput, GetChatHistoryOutput>,
        IGetChatHistoryTrain
{
    protected override Task<Either<Exception, GetChatHistoryOutput>> Junctions() =>
        Chain<FetchMessagesJunction>().Resolve();
}
