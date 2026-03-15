using Microsoft.Extensions.Logging;
using Trax.Core.Step;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Trains.JoinChatRoom.Steps;

public class AddParticipantStep(ChatDbContext db, ILogger<AddParticipantStep> logger)
    : Step<JoinChatRoomInput, JoinChatRoomOutput>
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
