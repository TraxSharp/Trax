namespace Trax.Samples.ChatService.Trains.SendMessage;

/// <summary>A message to post. The sender is the authenticated caller.</summary>
public record SendMessageInput
{
    public Guid ChatRoomId { get; init; }
    public required string Content { get; init; }
}
