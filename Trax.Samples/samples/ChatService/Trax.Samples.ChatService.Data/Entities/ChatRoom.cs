namespace Trax.Samples.ChatService.Data.Entities;

public class ChatRoom
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string CreatedByUserId { get; set; }

    public ICollection<ChatParticipant> Participants { get; set; } = [];
    public ICollection<ChatMessage> Messages { get; set; } = [];
}
