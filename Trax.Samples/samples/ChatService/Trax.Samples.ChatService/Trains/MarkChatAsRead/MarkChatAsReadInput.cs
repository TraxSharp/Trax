namespace Trax.Samples.ChatService.Trains.MarkChatAsRead;

public record MarkChatAsReadInput
{
    public Guid ChatRoomId { get; init; }
    public required string UserId { get; init; }
}
