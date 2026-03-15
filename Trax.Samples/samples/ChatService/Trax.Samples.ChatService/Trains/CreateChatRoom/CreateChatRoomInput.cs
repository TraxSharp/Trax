namespace Trax.Samples.ChatService.Trains.CreateChatRoom;

public record CreateChatRoomInput
{
    public required string Name { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
}
