using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Trax.Effect.Data.Utils;

/// <summary>
/// Ensures all DateTime values retrieved from the database have <see cref="DateTimeKind.Utc"/>.
/// </summary>
/// <remarks>
/// Values are passed through unchanged on write. On read, the kind is set to UTC.
/// Applied to all DateTime and DateTime? properties by each provider's OnModelCreating.
/// </remarks>
public class UtcValueConverter()
    : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
