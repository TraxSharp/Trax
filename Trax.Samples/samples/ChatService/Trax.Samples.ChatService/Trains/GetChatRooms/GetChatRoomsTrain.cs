using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ChatService.Trains.GetChatRooms.Steps;

namespace Trax.Samples.ChatService.Trains.GetChatRooms;

[TraxQuery(Description = "Lists chat rooms the user participates in")]
public class GetChatRoomsTrain
    : ServiceTrain<GetChatRoomsInput, GetChatRoomsOutput>,
        IGetChatRoomsTrain
{
    protected override async Task<Either<Exception, GetChatRoomsOutput>> RunInternal(
        GetChatRoomsInput input
    ) => Activate(input).Chain<FetchRoomsStep>().Resolve();
}
