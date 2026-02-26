using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.BackgroundJob;

/// <summary>
/// Provides Entity Framework Core configuration for the BackgroundJob model.
/// </summary>
public class PersistentBackgroundJob : Effect.Models.BackgroundJob.BackgroundJob
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Effect.Models.BackgroundJob.BackgroundJob>(entity =>
        {
            entity.ToTable("background_job", "trax");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Input).HasColumnType("jsonb");
        });
    }
}
