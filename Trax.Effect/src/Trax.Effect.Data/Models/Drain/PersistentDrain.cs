using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.Drain.Drain;

namespace Trax.Effect.Data.Models.Drain;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.Drain.Drain"/>, the singleton <c>trax.drain</c>
/// row. Mapped by name in <c>DataContext.OnModelCreating</c>, as the host heartbeat is.
/// </summary>
internal sealed class PersistentDrain : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("drain", "trax");
            entity.HasKey(e => e.Id);
            // The caller supplies the singleton id, as the migration's CHECK requires.
            entity.Property(e => e.Id).ValueGeneratedNever();
        });
    }
}
