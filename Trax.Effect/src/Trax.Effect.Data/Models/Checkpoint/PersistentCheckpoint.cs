using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.Checkpoint.Checkpoint;
using MetadataModel = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Data.Models.Checkpoint;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.Checkpoint.Checkpoint"/>,
/// the <c>trax.checkpoint</c> table.
/// </summary>
[System.Diagnostics.CodeAnalysis.Experimental("TRAXEXP003")]
public class PersistentCheckpoint : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("checkpoint", "trax");
            entity.HasKey(e => e.Id);

            // Deleted with its run, by the database, so every existing delete of metadata keeps
            // working without knowing this table exists.
            entity
                .HasOne<MetadataModel>()
                .WithMany()
                .HasForeignKey(e => e.MetadataId)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per run and declared node (Postgres migration 074, Sqlite 036). Its leading
            // column is the run, so it also serves the cascade and every lookup of a run's rows.
            entity
                .HasIndex(e => new { e.MetadataId, e.NodeId })
                .IsUnique()
                .HasDatabaseName("uq_checkpoint_run_node");

            entity.Property(e => e.State).HasColumnType("jsonb");
            entity.Property(e => e.Tracks).HasColumnType("jsonb");
        });
    }
}
