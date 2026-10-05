using Microsoft.Extensions.Logging;
using Trax.Api.Auth;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;
using Trax.Samples.ChatService.People;

namespace Trax.Samples.ChatService.Trains.InviteToChatRoom.Junctions;

/// <summary>Adds the invitee under the name the directory holds for them.</summary>
public class AddInviteeJunction(
    ChatDbContext db,
    TraxPrincipal caller,
    ChatDirectory directory,
    ILogger<AddInviteeJunction> logger
) : Junction<InviteToChatRoomInput, InviteToChatRoomOutput>
{
    public override async Task<InviteToChatRoomOutput> Run(InviteToChatRoomInput input)
    {
        var invitee = directory.Find(input.UserId)!;
        var now = DateTime.UtcNow;

        db.ChatParticipants.Add(
            new ChatParticipant
            {
                Id = Guid.NewGuid(),
                ChatRoomId = input.ChatRoomId,
                UserId = invitee.UserId,
                DisplayName = invitee.DisplayName,
                JoinedAt = now,
            }
        );
        await db.SaveChangesAsync();

        logger.LogInformation(
            "User {CallerId} added {UserId} to room {ChatRoomId}",
            caller.Id,
            invitee.UserId,
            input.ChatRoomId
        );

        return new InviteToChatRoomOutput
        {
            ChatRoomId = input.ChatRoomId,
            UserId = invitee.UserId,
            DisplayName = invitee.DisplayName,
            InvitedByDisplayName = caller.DisplayName,
            JoinedAt = now,
        };
    }
}
