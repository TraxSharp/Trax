using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.DeadLetter;

/// <summary>
/// EF Core mapping for <see cref="Effect.Models.DeadLetter.DeadLetter"/>: maps it to <c>trax.dead_letter</c>,
/// indexed on the manifest id, with a restricting foreign key to its manifest. Infrastructure applied by the data context; not intended for direct use. Query the rows
/// through <c>IDataContext</c>.
/// </summary>
internal class PersistentDeadLetter : Effect.Models.DeadLetter.DeadLetter
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
