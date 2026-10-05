using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.GetChatRooms.Junctions;

/// <summary>Lists the rooms the caller participates in, most recently active first.</summary>
public class FetchRoomsJunction(ChatDbContext db, TraxPrincipal caller)
    : Junction<GetChatRoomsInput, GetChatRoomsOutput>
{
    public override async Task<GetChatRoomsOutput> Run(GetChatRoomsInput input)
    {
        var rooms = await db
            .ChatParticipants.Where(p => p.UserId == caller.Id)
            .Select(p => new ChatRoomSummary
            {
                Id = p.ChatRoom.Id,
                Name = p.ChatRoom.Name,
                ParticipantCount = p.ChatRoom.Participants.Count,
                LastMessageAt = p.ChatRoom.Messages.Max(m => (DateTime?)m.SentAt),
            })
            .OrderByDescending(r => r.LastMessageAt)
            .ToListAsync();

        return new GetChatRoomsOutput { Rooms = rooms };
    }
}
