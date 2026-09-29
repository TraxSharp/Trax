using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.Log;

/// <summary>
/// EF Core mapping for <see cref="Effect.Models.Log.Log"/>: maps it to <c>trax.log</c>,
/// indexed on the metadata id. Infrastructure applied by the data context; not intended for direct use. Query the rows
/// through <c>IDataContext</c>.
/// </summary>
internal class PersistentLog : Effect.Models.Log.Log
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
