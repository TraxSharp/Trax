using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Api.GraphQL.Client.Utils.Converters;

public class DateOnlyConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType is JsonTokenType.Null or JsonTokenType.None
            ? new DateOnly()
            : DateOnly.FromDateTime(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToDateTime(new TimeOnly(0, 0, 0, 0), DateTimeKind.Utc));
    }
}
