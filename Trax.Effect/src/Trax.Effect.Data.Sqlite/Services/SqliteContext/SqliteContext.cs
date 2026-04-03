using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Utils;

namespace Trax.Effect.Data.Sqlite.Services.SqliteContext;

/// <summary>
/// SQLite-specific EF Core context. Strips the "trax" schema (not supported by SQLite),
/// remaps JSONB columns to TEXT, and applies UTC DateTime conversion.
/// </summary>
public class SqliteContext(DbContextOptions<SqliteContext> options)
    : DataContext<SqliteContext>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // SQLite has no schema support — remove from all entities
            entityType.SetSchema(null);

            foreach (var property in entityType.GetProperties())
            {
                // Remap Postgres JSONB columns to TEXT
                if (property.GetColumnType() == "jsonb")
                    property.SetColumnType("TEXT");

                // Ensure all DateTime values round-trip as UTC
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(new UtcValueConverter());
            }
        }
    }
}
