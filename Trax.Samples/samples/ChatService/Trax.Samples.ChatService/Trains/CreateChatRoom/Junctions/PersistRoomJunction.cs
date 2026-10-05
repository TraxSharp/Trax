using Microsoft.Extensions.Logging;
using Trax.Api.Auth;
using Trax.Core.Junction;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Trains.CreateChatRoom.Junctions;

/// <summary>
/// Creates the room and adds the caller as its first participant. The caller is the injected
/// <see cref="TraxPrincipal"/>: the train is <c>[TraxAuthorize]</c>, so an anonymous request is
/// refused before this junction is built.
/// </summary>
public class PersistRoomJunction(
    ChatDbContext db,
    TraxPrincipal caller,
    ILogger<PersistRoomJunction> logger
) : Junction<CreateChatRoomInput, CreateChatRoomOutput>
{
    public override async Task<CreateChatRoomOutput> Run(CreateChatRoomInput input)
    {
        var now = DateTime.UtcNow;

        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = input.Name,
            CreatedAt = now,
            CreatedByUserId = caller.Id,
        };

        var participant = new ChatParticipant
        {
            Id = Guid.NewGuid(),
            ChatRoomId = room.Id,
            UserId = caller.Id,
            DisplayName = caller.DisplayName,
            JoinedAt = now,
        };

        db.ChatRooms.Add(room);
        db.ChatParticipants.Add(participant);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Created chat room {RoomId} '{Name}' with creator {UserId}",
            room.Id,
            room.Name,
            caller.Id
        );

        return new CreateChatRoomOutput
        {
            ChatRoomId = room.Id,
            Name = room.Name,
            CreatedAt = room.CreatedAt,
        };
    }
}
