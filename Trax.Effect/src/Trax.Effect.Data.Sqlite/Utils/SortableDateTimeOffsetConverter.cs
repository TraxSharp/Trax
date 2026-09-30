using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Trax.Effect.Data.Sqlite.Utils;

/// <summary>
/// Stores a <see cref="DateTimeOffset"/> as fixed-width UTC text (<c>yyyy-MM-dd HH:mm:ss.fffffff+00:00</c>), so
/// that SQLite, which compares text byte by byte, orders the column in time order and EF can translate a
/// comparison against it. Reading accepts any offset, including the variable-width text EF writes by default,
/// so rows written before the conversion still load.
/// </summary>
internal sealed class SortableDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, string>(
        value => value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
        text =>
            DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
    )
{
    private const string Format = "yyyy'-'MM'-'dd' 'HH':'mm':'ss'.'fffffff'+00:00'";
}
