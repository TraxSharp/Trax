namespace Trax.Samples.ChatService.Trains.JoinChatRoom;

public record JoinChatRoomOutput
{
    public Guid ChatRoomId { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public DateTime JoinedAt { get; init; }
}
