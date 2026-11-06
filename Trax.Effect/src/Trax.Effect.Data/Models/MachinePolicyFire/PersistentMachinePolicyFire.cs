using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.MachinePolicyFire.MachinePolicyFire;

namespace Trax.Effect.Data.Models.MachinePolicyFire;

/// <summary>
/// Maps <c>trax.machine_policy_fire</c> (Postgres migration 090, Sqlite 049). No foreign key to snapshot_draft: a row
/// whose source or target was deleted is dropped when it is fired, and a key would depend on pk_snapshot_draft, which an
/// earlier script drops and rebuilds when it runs again (effect/0014).
/// </summary>
internal class PersistentMachinePolicyFire : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("machine_policy_fire", "trax");
            entity.HasKey(e => e.Id).HasName("pk_machine_policy_fire");
            // The runner reads the oldest rows of each policy first.
            entity
                .HasIndex(e => new { e.Policy, e.Id })
                .HasDatabaseName("ix_machine_policy_fire_policy");
        });
    }
}
