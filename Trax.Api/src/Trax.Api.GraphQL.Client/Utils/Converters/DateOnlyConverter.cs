using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Api.GraphQL.Client.Utils.Converters;

/// <summary>
/// Reads and writes <see cref="DateOnly"/> for the GraphQL client's default JSON options. Reads an
/// ISO-8601 date or date-time string (a JSON <c>null</c> reads as <see cref="DateOnly.MinValue"/>) and writes
/// midnight UTC as a full ISO-8601 date-time. Infrastructure not intended to be used directly.
/// </summary>
internal class DateOnlyConverter : JsonConverter<DateOnly>
{
    /// <inheritdoc/>
    public override DateOnly Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType is JsonTokenType.Null or JsonTokenType.None
            ? new DateOnly()
            : DateOnly.FromDateTime(reader.GetDateTime());

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToDateTime(new TimeOnly(0, 0, 0, 0), DateTimeKind.Utc));
    }
}
