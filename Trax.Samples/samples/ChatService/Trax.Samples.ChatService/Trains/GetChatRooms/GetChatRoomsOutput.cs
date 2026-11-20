namespace Trax.Samples.ChatService.Trains.GetChatRooms;

public record GetChatRoomsOutput
{
    public required List<ChatRoomSummary> Rooms { get; init; }
}
