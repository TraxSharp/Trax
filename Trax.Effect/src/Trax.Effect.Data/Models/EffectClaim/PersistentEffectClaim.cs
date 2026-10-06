using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.EffectClaim.EffectClaim;

namespace Trax.Effect.Data.Models.EffectClaim;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.EffectClaim.EffectClaim"/>: a feature
/// package's table, keyed by a string, mapped on the core data context like
/// <see cref="Trax.Effect.Data.Models.RunnerNonce.PersistentRunnerNonce"/>.
/// </summary>
internal class PersistentEffectClaim : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("effect_claim", "trax");
            entity.HasKey(e => e.EffectKey).HasName("pk_effect_claim");
        });
    }
}
