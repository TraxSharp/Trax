using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.GetChatHistory.Junctions;

/// <summary>
/// Returns a room's most recent messages, oldest first, to a caller who participates in the room.
/// <see cref="GetChatHistoryInput.Take"/> is clamped to 1..<see cref="GetChatHistoryInput.MaxTake"/>.
/// </summary>
public class FetchMessagesJunction(ChatDbContext db, TraxPrincipal caller)
    : Junction<GetChatHistoryInput, GetChatHistoryOutput>
{
    public override async Task<GetChatHistoryOutput> Run(GetChatHistoryInput input)
    {
        if (
            !await db.ChatParticipants.AnyAsync(p =>
                p.ChatRoomId == input.ChatRoomId && p.UserId == caller.Id
            )
        )
            throw new TrainException($"You are not a participant in room {input.ChatRoomId}.");

        var take = Math.Clamp(
            input.Take ?? GetChatHistoryInput.DefaultTake,
            1,
            GetChatHistoryInput.MaxTake
        );

        var query = db.ChatMessages.Where(m => m.ChatRoomId == input.ChatRoomId);
        if (input.Before.HasValue)
            query = query.Where(m => m.SentAt < input.Before.Value);

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .Select(m => new ChatMessageDto
            {
                Id = m.Id,
                SenderUserId = m.SenderUserId,
                SenderDisplayName = m.SenderDisplayName,
                Content = m.Content,
                SentAt = m.SentAt,
            })
            .ToListAsync();

        messages.Reverse();
        return new GetChatHistoryOutput { Messages = messages };
    }
}
