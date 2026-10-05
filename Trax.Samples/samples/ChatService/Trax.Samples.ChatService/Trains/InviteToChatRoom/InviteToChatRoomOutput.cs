namespace Trax.Samples.ChatService.Trains.InviteToChatRoom;

public record InviteToChatRoomOutput
{
    public Guid ChatRoomId { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string InvitedByDisplayName { get; init; }
    public DateTime JoinedAt { get; init; }
}
