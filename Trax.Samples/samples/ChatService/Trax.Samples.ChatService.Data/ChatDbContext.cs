using Microsoft.EntityFrameworkCore;
using Trax.Samples.ChatService.Data.Entities;

namespace Trax.Samples.ChatService.Data;

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();
    public DbSet<ChatParticipant> ChatParticipants => Set<ChatParticipant>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("chat");

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
