namespace Trax.Samples.ChatService.Data.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatRoomId { get; set; }
    public required string SenderUserId { get; set; }
    public required string SenderDisplayName { get; set; }
    public required string Content { get; set; }
    public DateTime SentAt { get; set; }

    public ChatRoom ChatRoom { get; set; } = null!;
}
