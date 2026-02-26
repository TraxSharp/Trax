using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Trax.Effect.Data.Postgres.Utils;

/// <summary>
/// Provides a value converter for DateTime properties that ensures all DateTime values are treated as UTC.
/// </summary>
/// <remarks>
/// The UtcValueConverter class is a specialized value converter for Entity Framework Core
/// that ensures all DateTime values retrieved from the database are treated as UTC.
///
/// This converter:
/// 1. Preserves the DateTime value when writing to the database
/// 2. Ensures the DateTime value has the UTC kind when reading from the database
///
/// This is particularly important for distributed systems and applications that
/// need to handle dates and times consistently across different timezones.
///
/// The converter is applied to all DateTime and nullable DateTime properties in the model
/// by the ApplyUtcDateTimeConverter extension method in ModelBuilderExtensions.
/// </remarks>
public class UtcValueConverter()
    : ValueConverter<DateTime, DateTime>(
        // Convert going to the database (no conversion needed)
        v => v,
        // Convert coming from the database (ensure UTC kind)
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    ) { }
