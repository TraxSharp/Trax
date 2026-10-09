using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.SnapshotDraft.SnapshotDraft;

namespace Trax.Effect.Data.Models.SnapshotDraft;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.SnapshotDraft.SnapshotDraft"/>: a feature
/// package's table, mapped on the core data context like
/// <see cref="Trax.Effect.Data.Models.RunnerNonce.PersistentRunnerNonce"/>.
/// </summary>
internal class PersistentSnapshotDraft : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("snapshot_draft", "trax");
            // A system row has no user key, and a key column cannot be null, so the primary key is a surrogate.
            // Identity is two partial unique indexes the migrations create: (user_key, machine, id) among user
            // rows, where the client-chosen id is unique only per user and machine, and (machine, id) among
            // system rows. Their filters differ by provider (an enum label on Postgres, an integer on Sqlite), so
            // the model leaves them to the migrations rather than declaring a filter only one provider can read.
            entity.HasKey(e => e.RowId).HasName("pk_snapshot_draft");
            entity.Property(e => e.RowId).ValueGeneratedOnAdd();
            entity
                .HasIndex(e => e.InvokeToken)
                .IsUnique()
                .HasDatabaseName("ux_snapshot_draft_invoke_token");
            // The operator listing reads newest first by updated_at, under one machine and state or across
            // them all. Postgres's migration also includes owner_kind in the first, which the model cannot
            // say for every provider.
            entity
                .HasIndex(e => new
                {
                    e.Machine,
                    e.State,
                    e.UpdatedAt,
                    e.RowId,
                })
                .IsDescending(false, false, true, true)
                .HasDatabaseName("ix_snapshot_draft_machine_state_updated");
            entity
                .HasIndex(e => new { e.UpdatedAt, e.RowId })
                .IsDescending(true, true)
                .HasDatabaseName("ix_snapshot_draft_updated");
            entity.Property(e => e.ConcurrencyToken).IsConcurrencyToken();
        });
    }
}
