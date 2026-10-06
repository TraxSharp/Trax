namespace Trax.Samples.ChatService.Trains.GetChatRoomPeople;

public record GetChatRoomPeopleOutput
{
    public required List<ChatRoomPerson> People { get; init; }
}

/// <summary>Someone the caller could invite, and whether they are in the room already.</summary>
public record ChatRoomPerson
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public bool IsMember { get; init; }
}
