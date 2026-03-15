using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Trains.GetChatRooms.Steps;

public class FetchRoomsStep(ChatDbContext db, ILogger<FetchRoomsStep> logger)
    : Step<GetChatRoomsInput, GetChatRoomsOutput>
{
    public override async Task<GetChatRoomsOutput> Run(GetChatRoomsInput input)
    {
        logger.LogInformation("Fetching chat rooms for user {UserId}", input.UserId);

        var rooms = await db
            .ChatParticipants.Where(p => p.UserId == input.UserId)
            .Select(p => new ChatRoomSummary
            {
                Id = p.ChatRoom.Id,
                Name = p.ChatRoom.Name,
                ParticipantCount = p.ChatRoom.Participants.Count,
                LastMessageAt = p.ChatRoom.Messages.Max(m => (DateTime?)m.SentAt),
                UnreadCount =
                    p.LastReadAt == null
                        ? p.ChatRoom.Messages.Count
                        : p.ChatRoom.Messages.Count(m => m.SentAt > p.LastReadAt),
            })
            .OrderByDescending(r => r.LastMessageAt)
            .ToListAsync();

        logger.LogInformation("Found {Count} rooms for user {UserId}", rooms.Count, input.UserId);

        return new GetChatRoomsOutput { Rooms = rooms };
    }
}
