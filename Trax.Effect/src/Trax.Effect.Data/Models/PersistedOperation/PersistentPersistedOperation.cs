using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.PersistedOperation.PersistedOperation;

namespace Trax.Effect.Data.Models.PersistedOperation;

/// <summary>
/// Provides EF Core configuration for
/// <see cref="Trax.Effect.Models.PersistedOperation.PersistedOperation"/>.
/// Mirrors the <see cref="Trax.Effect.Data.Models.Manifest.PersistentManifest"/>
/// pattern.
/// </summary>
public class PersistentPersistedOperation : BaseModel
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
            entity.ToTable("persisted_operation", "trax");
            entity.HasKey(e => new { e.TenantKey, e.Id });

            // tenant_key is NOT NULL in the schema; the storage layer
            // normalizes null at the boundary so callers can pass null.
            entity.Property(e => e.TenantKey).HasDefaultValueSql("''").IsRequired();

            entity.Property(e => e.Id).IsRequired();
            entity.Property(e => e.OperationName).IsRequired();
            entity.Property(e => e.Document).HasColumnType("text").IsRequired();
            entity.Property(e => e.ShapeFingerprint).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity
                .HasIndex(e => new { e.TenantKey, e.Id })
                .HasFilter("is_active = true")
                .HasDatabaseName("persisted_operation_active_idx");
        });
    }
}
