namespace Trax.Samples.ChatService.Trains.SendMessage;

public record SendMessageOutput
{
    public Guid MessageId { get; init; }
    public Guid ChatRoomId { get; init; }
    public required string SenderUserId { get; init; }
    public required string SenderDisplayName { get; init; }
    public required string Content { get; init; }
    public DateTime SentAt { get; init; }
}
