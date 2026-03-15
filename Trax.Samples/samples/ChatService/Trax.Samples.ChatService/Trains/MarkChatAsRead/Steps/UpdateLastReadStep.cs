using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.MarkChatAsRead.Steps;

public class UpdateLastReadStep(ChatDbContext db, ILogger<UpdateLastReadStep> logger)
    : Step<MarkChatAsReadInput, Unit>
{
    public override async Task<Unit> Run(MarkChatAsReadInput input)
    {
        var participant = await db.ChatParticipants.FirstOrDefaultAsync(p =>
            p.ChatRoomId == input.ChatRoomId && p.UserId == input.UserId
        );

        if (participant is null)
            throw new InvalidOperationException(
                $"User {input.UserId} is not a participant in room {input.ChatRoomId}."
            );

        participant.LastReadAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Marked room {ChatRoomId} as read for user {UserId}",
            input.ChatRoomId,
            input.UserId
        );

        return Unit.Default;
    }
}
