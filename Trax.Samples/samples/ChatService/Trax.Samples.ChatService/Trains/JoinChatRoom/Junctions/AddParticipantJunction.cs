using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

public class AddParticipantJunction(ChatDbContext db, ILogger<AddParticipantJunction> logger)
    : Junction<JoinChatRoomInput, JoinChatRoomOutput>
{
    public override async Task<JoinChatRoomOutput> Run(JoinChatRoomInput input)
    {
        var now = DateTime.UtcNow;

        var participant = new ChatParticipant
        {
            Id = Guid.NewGuid(),
            ChatRoomId = input.ChatRoomId,
            UserId = input.UserId,
            DisplayName = input.DisplayName,
            JoinedAt = now,
            LastReadAt = now,
        };

        db.ChatParticipants.Add(participant);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "User {UserId} joined room {ChatRoomId}",
            input.UserId,
            input.ChatRoomId
        );

        return new JoinChatRoomOutput
        {
            ChatRoomId = input.ChatRoomId,
            UserId = input.UserId,
            DisplayName = input.DisplayName,
            JoinedAt = now,
        };
    }
}
