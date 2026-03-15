namespace Trax.Samples.ChatService.Trains.JoinChatRoom;

public record JoinChatRoomInput
{
    public Guid ChatRoomId { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
}
