using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.WorkQueue;

/// <summary>
/// Provides Entity Framework Core configuration for the WorkQueue model.
/// </summary>
public class PersistentWorkQueue : Effect.Models.WorkQueue.WorkQueue
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Effect.Models.WorkQueue.WorkQueue>(entity =>
        {
            entity.ToTable("work_queue", "trax");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.ManifestId);

            entity.Property(e => e.Input).HasColumnType("jsonb");

            entity
                .HasOne(x => x.Manifest)
                .WithMany(m => m.WorkQueues)
                .HasForeignKey(x => x.ManifestId)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(x => x.Metadata)
                .WithMany()
                .HasForeignKey(x => x.MetadataId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
