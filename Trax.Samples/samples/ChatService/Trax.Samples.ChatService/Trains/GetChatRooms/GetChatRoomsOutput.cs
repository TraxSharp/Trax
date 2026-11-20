namespace Trax.Samples.ChatService.Trains.GetChatRooms;

public record GetChatRoomsOutput
{
    public required IReadOnlyList<ChatRoomSummary> Rooms { get; init; }
}
