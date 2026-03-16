using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Trains.SendMessage.Junctions;

public class PersistMessageJunction(ChatDbContext db, ILogger<PersistMessageJunction> logger)
    : Junction<SendMessageInput, SendMessageOutput>
{
    public override async Task<SendMessageOutput> Run(SendMessageInput input)
    {
        var participant = await db.ChatParticipants.FirstAsync(p =>
            p.ChatRoomId == input.ChatRoomId && p.UserId == input.SenderUserId
        );

        var now = DateTime.UtcNow;

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatRoomId = input.ChatRoomId,
            SenderUserId = input.SenderUserId,
            SenderDisplayName = participant.DisplayName,
            Content = input.Content,
            SentAt = now,
        };

        db.ChatMessages.Add(message);

        // Mark sender's own messages as read
        participant.LastReadAt = now;

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Message {MessageId} sent by {UserId} in room {ChatRoomId}",
            message.Id,
            input.SenderUserId,
            input.ChatRoomId
        );

        return new SendMessageOutput
        {
            MessageId = message.Id,
            ChatRoomId = input.ChatRoomId,
            SenderUserId = input.SenderUserId,
            SenderDisplayName = participant.DisplayName,
            Content = input.Content,
            SentAt = now,
        };
    }
}
