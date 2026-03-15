namespace Trax.Samples.ChatService.Trains.GetChatHistory;

public record GetChatHistoryInput
{
    public Guid ChatRoomId { get; init; }
    public int Take { get; init; } = 50;
    public DateTime? Before { get; init; }
}
