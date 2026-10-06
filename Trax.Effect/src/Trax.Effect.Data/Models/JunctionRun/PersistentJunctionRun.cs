using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.JunctionRun.JunctionRun;
using MetadataModel = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Data.Models.JunctionRun;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.JunctionRun.JunctionRun"/>,
/// the <c>trax.junction_run</c> table.
/// </summary>
public class PersistentJunctionRun : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("junction_run", "trax");
            entity.HasKey(e => e.Id);

            // Deleted with its run, by the database, so every existing delete of metadata keeps
            // working without knowing this table exists.
            entity
                .HasOne<MetadataModel>()
                .WithMany()
                .HasForeignKey(e => e.MetadataId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasIndex(e => new { e.MetadataId, e.Position })
                .IsUnique()
                .HasDatabaseName("uq_junction_run_position");
        });
    }
}
