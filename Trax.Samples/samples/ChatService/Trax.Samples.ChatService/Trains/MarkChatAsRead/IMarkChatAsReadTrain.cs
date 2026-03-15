using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ChatService.Trains.MarkChatAsRead;

public interface IMarkChatAsReadTrain : IServiceTrain<MarkChatAsReadInput, Unit>;
