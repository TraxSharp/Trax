using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.WorkQueue;

/// <summary>
/// Provides Entity Framework Core configuration for the WorkQueue model.
/// </summary>
internal class PersistentWorkQueue : Effect.Models.WorkQueue.WorkQueue
{
    // Holds the EF configuration and is never instantiated. Private so it is not a way around
    // WorkQueue.Create, which is the only way to build an entry that dispatches.
    private PersistentWorkQueue() { }

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Effect.Models.WorkQueue.WorkQueue>(entity =>
        {
            entity.ToTable("work_queue", "trax");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.ManifestId);

            // Compared exactly and indexed in SQL only (ix_work_queue_external_id, the subject
            // indexes), so the Postgres NUL scrubber must leave them as written. The literal is
            // NulCharacterInterceptor.ComparedExactlyAnnotation.
            entity.Property(e => e.ExternalId).HasAnnotation("Trax:ComparedExactly", true);
            entity.Property(e => e.SubjectKey).HasAnnotation("Trax:ComparedExactly", true);

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

            entity
                .HasOne(x => x.DeadLetter)
                .WithMany()
                .HasForeignKey(x => x.DeadLetterId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
