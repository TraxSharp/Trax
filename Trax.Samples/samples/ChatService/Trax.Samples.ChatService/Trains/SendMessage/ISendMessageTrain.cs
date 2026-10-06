using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ChatService.Trains.SendMessage;

public interface ISendMessageTrain : IServiceTrain<SendMessageInput, SendMessageOutput>;
