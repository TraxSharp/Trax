using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.HostHeartbeat.HostHeartbeat;

namespace Trax.Effect.Data.Models.HostHeartbeat;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.HostHeartbeat.HostHeartbeat"/>,
/// the <c>trax.host_heartbeat</c> table. Keyed by a string, so it is mapped by name in
/// <c>DataContext.OnModelCreating</c>, as the runner nonce is.
/// </summary>
internal class PersistentHostHeartbeat : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("host_heartbeat", "trax");
            entity.HasKey(e => e.InstanceId);

            // The prune deletes by age, and the hosts read orders by it (Postgres 086, Sqlite 045).
            entity
                .HasIndex(e => e.LastHeartbeatAt)
                .HasDatabaseName("ix_host_heartbeat_last_heartbeat_at");
        });
    }
}
