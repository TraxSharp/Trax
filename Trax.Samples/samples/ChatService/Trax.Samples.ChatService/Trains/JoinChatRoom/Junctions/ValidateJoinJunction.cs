using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

public class ValidateJoinJunction(ChatDbContext db, ILogger<ValidateJoinJunction> logger)
    : Junction<JoinChatRoomInput, JoinChatRoomInput>
{
    public override async Task<JoinChatRoomInput> Run(JoinChatRoomInput input)
    {
        var roomExists = await db.ChatRooms.AnyAsync(r => r.Id == input.ChatRoomId);
        if (!roomExists)
            throw new InvalidOperationException($"Chat room {input.ChatRoomId} does not exist.");

        var alreadyJoined = await db.ChatParticipants.AnyAsync(p =>
            p.ChatRoomId == input.ChatRoomId && p.UserId == input.UserId
        );
        if (alreadyJoined)
            throw new InvalidOperationException(
                $"User {input.UserId} is already a participant in room {input.ChatRoomId}."
            );

        logger.LogInformation(
            "Validated join: user {UserId} can join room {ChatRoomId}",
            input.UserId,
            input.ChatRoomId
        );

        return input;
    }
}
