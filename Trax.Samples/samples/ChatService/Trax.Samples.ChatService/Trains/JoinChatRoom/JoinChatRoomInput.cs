namespace Trax.Samples.ChatService.Trains.JoinChatRoom;

/// <summary>The room to join. The caller joins as themselves.</summary>
public record JoinChatRoomInput
{
    public Guid ChatRoomId { get; init; }
}
