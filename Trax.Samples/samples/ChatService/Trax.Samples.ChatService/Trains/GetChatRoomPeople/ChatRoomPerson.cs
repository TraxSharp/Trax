namespace Trax.Samples.ChatService.Trains.GetChatRoomPeople;

/// <summary>Someone the caller could invite, and whether they are in the room already.</summary>
public record ChatRoomPerson
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public bool IsMember { get; init; }
}
