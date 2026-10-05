namespace Trax.Samples.ChatService.Trains.CreateChatRoom;

/// <summary>
/// The room to create. The creator is the authenticated caller, never a field of the input, so a
/// caller cannot create a room in someone else's name.
/// </summary>
public record CreateChatRoomInput
{
    public required string Name { get; init; }
}
