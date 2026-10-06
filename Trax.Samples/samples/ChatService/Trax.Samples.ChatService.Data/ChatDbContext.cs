using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Data;

public class ChatDbContext(DbContextOptions<ChatDbContext> options)
    : DomainDataContext<ChatDbContext>(options),
        IChatDbContext
{
    public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();
    public DbSet<ChatParticipant> ChatParticipants => Set<ChatParticipant>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    // This sample runs on SQLite, which has no schema concept, so the base skips the default schema.
    // The name is declared anyway so switching the provider to PostgreSQL isolates the tables.
    protected override string Schema => "chat";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatRoom>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.CreatedByUserId).IsRequired();
        });

        modelBuilder.Entity<ChatParticipant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.DisplayName).IsRequired();

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.ChatRoomId, e.UserId }).IsUnique();

            entity
                .HasOne(e => e.ChatRoom)
                .WithMany(r => r.Participants)
                .HasForeignKey(e => e.ChatRoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SenderUserId).IsRequired();
            entity.Property(e => e.SenderDisplayName).IsRequired();
            entity.Property(e => e.Content).IsRequired();

            entity.HasIndex(e => new { e.ChatRoomId, e.SentAt });

            entity
                .HasOne(e => e.ChatRoom)
                .WithMany(r => r.Messages)
                .HasForeignKey(e => e.ChatRoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
