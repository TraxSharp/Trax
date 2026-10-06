using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom;

public interface IJoinChatRoomTrain : IServiceTrain<JoinChatRoomInput, JoinChatRoomOutput>;
