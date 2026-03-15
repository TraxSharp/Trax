namespace Trax.Samples.ChatService.Trains.GetChatRooms;

public record GetChatRoomsOutput
{
    public required List<ChatRoomSummary> Rooms { get; init; }
}

public record ChatRoomSummary
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public int ParticipantCount { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public int UnreadCount { get; init; }
}
