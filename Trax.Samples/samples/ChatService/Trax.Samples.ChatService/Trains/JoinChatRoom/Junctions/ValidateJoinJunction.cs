using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

public class ValidateJoinJunction(ChatDbContext db, TraxPrincipal caller)
    : Junction<JoinChatRoomInput, JoinChatRoomInput>
{
    public override async Task<JoinChatRoomInput> Run(JoinChatRoomInput input)
    {
        if (!await db.ChatRooms.AnyAsync(r => r.Id == input.ChatRoomId))
            throw new TrainException($"Chat room {input.ChatRoomId} does not exist.");

        if (
            await db.ChatParticipants.AnyAsync(p =>
                p.ChatRoomId == input.ChatRoomId && p.UserId == caller.Id
            )
        )
            throw new TrainException($"You are already a participant in room {input.ChatRoomId}.");

        return input;
    }
}
