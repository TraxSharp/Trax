namespace Trax.Samples.ChatService.Trains.GetChatRooms;

public record GetChatRoomsInput
{
    public required string UserId { get; init; }
}
