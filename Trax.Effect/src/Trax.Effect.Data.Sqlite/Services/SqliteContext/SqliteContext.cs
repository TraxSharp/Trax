using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Utils;
using Trax.Effect.Data.Utils;

namespace Trax.Effect.Data.Sqlite.Services.SqliteContext;

/// <summary>
/// SQLite-specific EF Core context. Strips the "trax" schema (not supported by SQLite),
/// remaps JSONB columns to TEXT, applies UTC DateTime conversion, and stores DateTimeOffset as
/// fixed-width UTC text so it compares in time order.
/// Infrastructure registered by <c>UseSqlite</c>; not intended to be constructed directly.
/// </summary>
internal class SqliteContext(DbContextOptions<SqliteContext> options)
    : DataContext<SqliteContext>(options)
{
    /// <summary>
    /// Builds the shared Trax model, then adapts it to SQLite: every entity loses its schema, every
    /// <c>jsonb</c> column becomes <c>TEXT</c>, every <see cref="DateTime"/> property is read
    /// back with <see cref="DateTimeKind.Utc"/>, and every <see cref="DateTimeOffset"/> property
    /// without a conversion of its own is stored as sortable UTC text.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
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

                // SQLite has no timestamp type, and EF refuses to compare a DateTimeOffset it stores
                // as text with its offset. Stored as fixed-width UTC text, the column compares in time
                // order, so a lease or expiry check translates to SQL. A property that already
                // converts (the runner nonce's Unix seconds) keeps its own.
                if (
                    (
                        property.ClrType == typeof(DateTimeOffset)
                        || property.ClrType == typeof(DateTimeOffset?)
                    ) && property.GetValueConverter() is null
                )
                    property.SetValueConverter(new SortableDateTimeOffsetConverter());
            }
        }
    }
}
