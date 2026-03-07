using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.DeadLetter;

public class PersistentDeadLetter : Effect.Models.DeadLetter.DeadLetter
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Effect.Models.DeadLetter.DeadLetter>(entity =>
        {
            entity.ToTable("dead_letter", "trax");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.ManifestId);

            entity
                .HasOne(x => x.Manifest)
                .WithMany(m => m.DeadLetters)
                .HasForeignKey(x => x.ManifestId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
