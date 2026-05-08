using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.SchedulerConfig;

/// <summary>
/// EF Core configuration for the singleton <c>scheduler_config</c> row.
/// </summary>
public class PersistentSchedulerConfig : Effect.Models.SchedulerConfig.SchedulerConfig
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Effect.Models.SchedulerConfig.SchedulerConfig>(entity =>
        {
            entity.ToTable("scheduler_config", "trax");
            entity.HasKey(e => e.Id);
            // No ValueGeneratedOnAdd: callers (and the migration) supply the singleton id.
        });
    }
}
