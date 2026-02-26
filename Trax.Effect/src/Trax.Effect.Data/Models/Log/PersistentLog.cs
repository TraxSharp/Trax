using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.Log;

public class PersistentLog : Effect.Models.Log.Log
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Effect.Models.Log.Log>(entity =>
        {
            entity.ToTable("log", "trax");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.MetadataId);
        });
    }
}
