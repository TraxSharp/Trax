using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.SnapshotTransition.SnapshotTransition;

namespace Trax.Effect.Data.Models.SnapshotTransition;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.SnapshotTransition.SnapshotTransition"/>, the
/// <c>trax.snapshot_transition</c> table: a feature package's table, mapped on the core data context like
/// <see cref="Trax.Effect.Data.Models.SnapshotDraft.PersistentSnapshotDraft"/>.
/// </summary>
internal class PersistentSnapshotTransition : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("snapshot_transition", "trax");
            entity.HasKey(e => e.Id).HasName("pk_snapshot_transition");

            // No foreign key: the migrations delete the history with the instance's row by a trigger on
            // snapshot_draft, so every delete of an instance (a discard, the expiry sweep, host code) takes it
            // without knowing this table exists. A foreign key would depend on pk_snapshot_draft, which an earlier
            // script drops and rebuilds when it runs again (effect/0014).

            // One row per revision of an incarnation (Postgres migration 089, Sqlite 048). Its leading column is
            // the row, so it serves the trigger's delete and the newest-first read of one instance's history.
            entity
                .HasIndex(e => new { e.DraftRowId, e.Revision })
                .IsUnique()
                .HasDatabaseName("uq_snapshot_transition_draft_revision");
        });
    }
}
