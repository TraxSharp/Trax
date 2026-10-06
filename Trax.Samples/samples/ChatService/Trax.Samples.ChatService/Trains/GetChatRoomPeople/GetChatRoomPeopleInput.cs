namespace Trax.Samples.ChatService.Trains.GetChatRoomPeople;

/// <summary>The room whose possible invitees to list. Only a member of the room may ask.</summary>
public record GetChatRoomPeopleInput
{
    public Guid ChatRoomId { get; init; }
}
