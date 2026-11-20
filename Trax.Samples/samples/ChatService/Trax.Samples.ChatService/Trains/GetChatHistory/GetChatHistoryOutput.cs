namespace Trax.Samples.ChatService.Trains.GetChatHistory;

public record GetChatHistoryOutput
{
    public required IReadOnlyList<ChatMessageDto> Messages { get; init; }
}
