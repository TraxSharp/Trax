namespace Trax.Samples.ChatService.Trains.GetChatHistory;

public record GetChatHistoryOutput
{
    public required List<ChatMessageDto> Messages { get; init; }
}
