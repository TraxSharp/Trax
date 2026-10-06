using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Data;

/// <summary>Companion interface for <see cref="ChatDbContext"/>.</summary>
public interface IChatDbContext : IDomainDataContext
{
    DbSet<ChatRoom> ChatRooms { get; }
    DbSet<ChatParticipant> ChatParticipants { get; }
    DbSet<ChatMessage> ChatMessages { get; }
}
