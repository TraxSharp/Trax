using Microsoft.Extensions.Logging;
using Trax.Core.Step;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Trains.CreateChatRoom.Steps;

public class PersistRoomStep(ChatDbContext db, ILogger<PersistRoomStep> logger)
    : Step<CreateChatRoomInput, CreateChatRoomOutput>
{
    public override async Task<CreateChatRoomOutput> Run(CreateChatRoomInput input)
    {
        var now = DateTime.UtcNow;

        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = input.Name,
            CreatedAt = now,
            CreatedByUserId = input.UserId,
        };

        var participant = new ChatParticipant
        {
            Id = Guid.NewGuid(),
            ChatRoomId = room.Id,
            UserId = input.UserId,
            DisplayName = input.DisplayName,
            JoinedAt = now,
            LastReadAt = now,
        };

        db.ChatRooms.Add(room);
        db.ChatParticipants.Add(participant);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Created chat room {RoomId} '{Name}' with creator {UserId}",
            room.Id,
            room.Name,
            input.UserId
        );

        return new CreateChatRoomOutput
        {
            ChatRoomId = room.Id,
            Name = room.Name,
            CreatedAt = room.CreatedAt,
        };
    }
}
