using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.People;

namespace Trax.Samples.ChatService.Trains.GetChatRoomPeople.Junctions;

/// <summary>
/// Lists everyone in the directory except the caller, each marked as in the room or not, to a caller
/// who is a member of the room.
/// </summary>
public class FetchRoomPeopleJunction(
    ChatDbContext db,
    TraxPrincipal caller,
    ChatDirectory directory
) : Junction<GetChatRoomPeopleInput, GetChatRoomPeopleOutput>
{
    public override async Task<GetChatRoomPeopleOutput> Run(GetChatRoomPeopleInput input)
    {
        var members = await db
            .ChatParticipants.Where(p => p.ChatRoomId == input.ChatRoomId)
            .Select(p => p.UserId)
            .ToListAsync();

        if (!members.Contains(caller.Id))
            throw new TrainException($"You are not a participant in room {input.ChatRoomId}.");

        var people = directory
            .All.Where(p => p.UserId != caller.Id)
            .OrderBy(p => p.DisplayName)
            .Select(p => new ChatRoomPerson
            {
                UserId = p.UserId,
                DisplayName = p.DisplayName,
                IsMember = members.Contains(p.UserId),
            })
            .ToList();

        return new GetChatRoomPeopleOutput { People = people };
    }
}
