namespace Trax.Samples.ChatService.Trains.InviteToChatRoom;

/// <summary>
/// The room, and who to add to it. Only the person is named: the caller invites as themselves, and
/// the server reads the invitee's display name from its own directory.
/// </summary>
public record InviteToChatRoomInput
{
    public Guid ChatRoomId { get; init; }
    public required string UserId { get; init; }
}
