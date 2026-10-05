using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.SendMessage.Junctions;

/// <summary>Refuses an empty message, and a caller who is not a participant in the room.</summary>
public class ValidateSenderJunction(ChatDbContext db, TraxPrincipal caller)
    : Junction<SendMessageInput, SendMessageInput>
{
    public override async Task<SendMessageInput> Run(SendMessageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Content))
            throw new TrainException("Message content cannot be empty.");

        if (
            !await db.ChatParticipants.AnyAsync(p =>
                p.ChatRoomId == input.ChatRoomId && p.UserId == caller.Id
            )
        )
            throw new TrainException($"You are not a participant in room {input.ChatRoomId}.");

        return input;
    }
}
