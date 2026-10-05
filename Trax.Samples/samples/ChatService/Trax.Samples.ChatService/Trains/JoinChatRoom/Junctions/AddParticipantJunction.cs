using Microsoft.Extensions.Logging;
using Trax.Api.Auth;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom.Junctions;

public class AddParticipantJunction(
    ChatDbContext db,
    TraxPrincipal caller,
    ILogger<AddParticipantJunction> logger
) : Junction<JoinChatRoomInput, JoinChatRoomOutput>
{
    public override async Task<JoinChatRoomOutput> Run(JoinChatRoomInput input)
    {
        var now = DateTime.UtcNow;

        db.ChatParticipants.Add(
            new ChatParticipant
            {
                Id = Guid.NewGuid(),
                ChatRoomId = input.ChatRoomId,
                UserId = caller.Id,
                DisplayName = caller.DisplayName,
                JoinedAt = now,
            }
        );
        await db.SaveChangesAsync();

        logger.LogInformation(
            "User {UserId} joined room {ChatRoomId}",
            caller.Id,
            input.ChatRoomId
        );

        return new JoinChatRoomOutput
        {
            ChatRoomId = input.ChatRoomId,
            UserId = caller.Id,
            DisplayName = caller.DisplayName,
            JoinedAt = now,
        };
    }
}
