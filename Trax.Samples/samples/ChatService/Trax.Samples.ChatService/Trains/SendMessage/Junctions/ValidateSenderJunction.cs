using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.SendMessage.Junctions;

public class ValidateSenderJunction(ChatDbContext db, ILogger<ValidateSenderJunction> logger)
    : Junction<SendMessageInput, SendMessageInput>
{
    public override async Task<SendMessageInput> Run(SendMessageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Content))
            throw new ArgumentException("Message content cannot be empty.");

        var isParticipant = await db.ChatParticipants.AnyAsync(p =>
            p.ChatRoomId == input.ChatRoomId && p.UserId == input.SenderUserId
        );

        if (!isParticipant)
            throw new InvalidOperationException(
                $"User {input.SenderUserId} is not a participant in room {input.ChatRoomId}."
            );

        logger.LogInformation(
            "Validated sender {UserId} in room {ChatRoomId}",
            input.SenderUserId,
            input.ChatRoomId
        );

        return input;
    }
}
