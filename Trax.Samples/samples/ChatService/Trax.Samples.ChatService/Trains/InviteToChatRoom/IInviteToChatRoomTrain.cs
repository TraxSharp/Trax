using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ChatService.Trains.InviteToChatRoom;

public interface IInviteToChatRoomTrain
    : IServiceTrain<InviteToChatRoomInput, InviteToChatRoomOutput>;
