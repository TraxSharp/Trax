using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ChatService.Trains.CreateChatRoom;

public interface ICreateChatRoomTrain : IServiceTrain<CreateChatRoomInput, CreateChatRoomOutput>;
