using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ChatService.Trains.GetChatRooms;

public interface IGetChatRoomsTrain : IServiceTrain<GetChatRoomsInput, GetChatRoomsOutput>;
