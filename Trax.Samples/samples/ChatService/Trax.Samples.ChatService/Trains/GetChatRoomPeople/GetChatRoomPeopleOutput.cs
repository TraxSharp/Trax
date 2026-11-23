namespace Trax.Samples.ChatService.Trains.GetChatRoomPeople;

public record GetChatRoomPeopleOutput
{
    public required IReadOnlyList<ChatRoomPerson> People { get; init; }
}
