using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.People;

namespace Trax.Samples.ChatService.Trains.InviteToChatRoom.Junctions;

/// <summary>
/// Admits an invite only from a member of the room, only for someone the directory knows, and only
/// for someone not already in it. Membership is the room's privacy: a room is reachable only through
/// someone already inside it.
/// </summary>
public class ValidateInviteJunction(ChatDbContext db, TraxPrincipal caller, ChatDirectory directory)
    : Junction<InviteToChatRoomInput, InviteToChatRoomInput>
{
    public override async Task<InviteToChatRoomInput> Run(InviteToChatRoomInput input)
    {
        if (!await db.ChatRooms.AnyAsync(r => r.Id == input.ChatRoomId))
            throw new TrainException($"Chat room {input.ChatRoomId} does not exist.");

        if (
            !await db.ChatParticipants.AnyAsync(p =>
                p.ChatRoomId == input.ChatRoomId && p.UserId == caller.Id
            )
        )
            throw new TrainException($"You are not a participant in room {input.ChatRoomId}.");

        if (input.UserId == caller.Id)
            throw new TrainException("You are already in this room.");

        if (directory.Find(input.UserId) is null)
            throw new TrainException($"There is no one called {input.UserId} to invite.");

        if (
            await db.ChatParticipants.AnyAsync(p =>
                p.ChatRoomId == input.ChatRoomId && p.UserId == input.UserId
            )
        )
            throw new TrainException($"{input.UserId} is already a participant in this room.");

        return input;
    }
}
