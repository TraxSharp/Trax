using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Api.Auth;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Trains.SendMessage.Junctions;

/// <summary>
/// Stores the message as the caller's. Its output carries <c>chatRoomId</c>, which is how
/// <c>ChatLifecycleHook</c> knows which room's subscribers to notify once the train completes.
/// </summary>
public class PersistMessageJunction(
    ChatDbContext db,
    TraxPrincipal caller,
    ILogger<PersistMessageJunction> logger
) : Junction<SendMessageInput, SendMessageOutput>
{
    public override async Task<SendMessageOutput> Run(SendMessageInput input)
    {
        var participant = await db.ChatParticipants.FirstAsync(p =>
            p.ChatRoomId == input.ChatRoomId && p.UserId == caller.Id
        );

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatRoomId = input.ChatRoomId,
            SenderUserId = caller.Id,
            SenderDisplayName = participant.DisplayName,
            Content = input.Content,
            SentAt = DateTime.UtcNow,
        };

        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Message {MessageId} sent by {UserId} in room {ChatRoomId}",
            message.Id,
            caller.Id,
            input.ChatRoomId
        );

        return new SendMessageOutput
        {
            MessageId = message.Id,
            ChatRoomId = input.ChatRoomId,
            SenderUserId = message.SenderUserId,
            SenderDisplayName = message.SenderDisplayName,
            Content = message.Content,
            SentAt = message.SentAt,
        };
    }
}
