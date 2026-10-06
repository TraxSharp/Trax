using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ChatService.Trains.GetChatHistory;

public interface IGetChatHistoryTrain : IServiceTrain<GetChatHistoryInput, GetChatHistoryOutput>;
