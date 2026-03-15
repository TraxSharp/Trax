namespace Trax.Samples.ChatService.Data.Entities;

public class ChatParticipant
{
    public Guid Id { get; set; }
    public Guid ChatRoomId { get; set; }
    public required string UserId { get; set; }
    public required string DisplayName { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LastReadAt { get; set; }

    public ChatRoom ChatRoom { get; set; } = null!;
}
