using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.GetChatHistory.Junctions;

public class FetchMessagesJunction(ChatDbContext db, ILogger<FetchMessagesJunction> logger)
    : Junction<GetChatHistoryInput, GetChatHistoryOutput>
{
    public override async Task<GetChatHistoryOutput> Run(GetChatHistoryInput input)
    {
        logger.LogInformation(
            "Fetching chat history for room {ChatRoomId}, take {Take}",
            input.ChatRoomId,
            input.Take
        );

        var query = db.ChatMessages.Where(m => m.ChatRoomId == input.ChatRoomId);

        if (input.Before.HasValue)
            query = query.Where(m => m.SentAt < input.Before.Value);

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .Take(input.Take)
            .Select(m => new ChatMessageDto
            {
                Id = m.Id,
                SenderUserId = m.SenderUserId,
                SenderDisplayName = m.SenderDisplayName,
                Content = m.Content,
                SentAt = m.SentAt,
            })
            .ToListAsync();

        // Return in chronological order
        messages.Reverse();

        logger.LogInformation(
            "Returning {Count} messages for room {ChatRoomId}",
            messages.Count,
            input.ChatRoomId
        );

        return new GetChatHistoryOutput { Messages = messages };
    }
}
