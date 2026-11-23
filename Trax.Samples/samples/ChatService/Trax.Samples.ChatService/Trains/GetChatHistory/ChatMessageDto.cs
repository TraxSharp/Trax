namespace Trax.Samples.ChatService.Trains.GetChatHistory;

public record ChatMessageDto
{
    public Guid Id { get; init; }
    public required string SenderUserId { get; init; }
    public required string SenderDisplayName { get; init; }
    public required string Content { get; init; }
    public DateTime SentAt { get; init; }
}
