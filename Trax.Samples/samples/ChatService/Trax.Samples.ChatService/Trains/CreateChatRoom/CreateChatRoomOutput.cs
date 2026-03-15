namespace Trax.Samples.ChatService.Trains.CreateChatRoom;

public record CreateChatRoomOutput
{
    public Guid ChatRoomId { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedAt { get; init; }
}
