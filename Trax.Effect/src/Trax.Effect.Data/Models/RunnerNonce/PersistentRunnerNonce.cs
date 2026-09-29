using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.RunnerNonce.RunnerNonce;

namespace Trax.Effect.Data.Models.RunnerNonce;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.RunnerNonce.RunnerNonce"/>.
/// Mirrors the <see cref="Trax.Effect.Data.Models.PersistedOperation.PersistentPersistedOperation"/>
/// pattern: a feature package's table, keyed by a string, mapped on the core data context.
/// </summary>
internal class PersistentRunnerNonce : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("runner_nonce", "trax");
            entity.HasKey(e => e.Nonce);

            // The column is Unix seconds (bigint on Postgres, INTEGER on Sqlite) so that expiry
            // compares as a number on both providers. The conversion is monotonic, so a comparison
            // against a DateTimeOffset translates to the same comparison of the stored seconds.
            entity
                .Property(e => e.ExpiresAt)
                .HasConversion(
                    value => value.ToUnixTimeSeconds(),
                    seconds => DateTimeOffset.FromUnixTimeSeconds(seconds)
                );

            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_runner_nonce_expires_at");
        });
    }
}
