using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ChatService.Trains.GetChatRoomPeople;

public interface IGetChatRoomPeopleTrain
    : IServiceTrain<GetChatRoomPeopleInput, GetChatRoomPeopleOutput>;
