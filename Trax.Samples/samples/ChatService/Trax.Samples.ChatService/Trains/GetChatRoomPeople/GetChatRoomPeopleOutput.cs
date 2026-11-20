namespace Trax.Samples.ChatService.Trains.GetChatRoomPeople;

public record GetChatRoomPeopleOutput
{
    public required List<ChatRoomPerson> People { get; init; }
}
