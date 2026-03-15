namespace Trax.Samples.ChatService.Trains.SendMessage;

public record SendMessageInput
{
    public Guid ChatRoomId { get; init; }
    public required string SenderUserId { get; init; }
    public required string Content { get; init; }
}
