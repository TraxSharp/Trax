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
            // The client-chosen id is unique only per user and machine: two machines may give one user a
            // draft under the same id. The key's leading column also serves the user-scoped reads, so no
            // separate user_key index is needed.
            entity
                .HasKey(e => new
                {
                    e.UserKey,
                    e.Machine,
                    e.Id,
                })
                .HasName("pk_snapshot_draft");
            entity.Property(e => e.ConcurrencyToken).IsConcurrencyToken();
        });
    }
}
