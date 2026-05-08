using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.PersistedOperationHistory.PersistedOperationHistory;

namespace Trax.Effect.Data.Models.PersistedOperationHistory;

/// <summary>
/// EF Core configuration for
/// <see cref="Trax.Effect.Models.PersistedOperationHistory.PersistedOperationHistory"/>.
/// </summary>
public class PersistentPersistedOperationHistory : BaseModel
{
    /// <summary>
    /// Apply EF Core mapping for <see cref="BaseModel"/>. Public because
    /// the GraphQL persisted-operations package consumes this from a
    /// different assembly to wire its dedicated DbContext.
    /// </summary>
    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("persisted_operation_history", "trax");
            entity.HasKey(e => e.HistoryId);
            entity.Property(e => e.HistoryId).ValueGeneratedOnAdd();

            entity.Property(e => e.TenantKey).HasDefaultValueSql("''").IsRequired();

            entity.Property(e => e.Id).IsRequired();
            entity.Property(e => e.Document).HasColumnType("text").IsRequired();
            entity.Property(e => e.ShapeFingerprint).IsRequired();
            entity.Property(e => e.ChangeType).IsRequired();
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("now()");

            entity
                .HasIndex(e => new
                {
                    e.TenantKey,
                    e.Id,
                    e.ChangedAt,
                })
                .IsDescending(false, false, true)
                .HasDatabaseName("persisted_operation_history_id_idx");
        });
    }
}
