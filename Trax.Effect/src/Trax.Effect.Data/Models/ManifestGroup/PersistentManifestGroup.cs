using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.ManifestGroup;

/// <summary>
/// Provides Entity Framework Core configuration for the ManifestGroup model.
/// </summary>
public class PersistentManifestGroup : Effect.Models.ManifestGroup.ManifestGroup
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Effect.Models.ManifestGroup.ManifestGroup>(entity =>
        {
            entity.ToTable("manifest_group", "trax");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.Name).IsUnique();

            entity
                .HasMany(g => g.Manifests)
                .WithOne(m => m.ManifestGroup)
                .HasForeignKey(m => m.ManifestGroupId);
        });
    }
}
