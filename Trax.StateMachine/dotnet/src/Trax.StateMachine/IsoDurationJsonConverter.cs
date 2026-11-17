using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Trax.StateMachine;

/// <summary>
/// A <see cref="TimeSpan"/> as JSON: the ISO-8601 duration a timer reads (<c>"PT5M"</c>), never .NET's
/// <c>"00:05:00"</c>. A context field a timer reads its duration from is written and read through this, by the engine
/// and by the typed machine surface alike. Text outside the subset <see cref="IsoDuration"/> parses is a
/// <see cref="JsonException"/>, which a reader answers as invalid input.
/// </summary>
internal sealed class IsoDurationJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.String
        && IsoDuration.TryParse(reader.GetString(), out var duration)
            ? duration
            : throw new JsonException("Not an ISO-8601 duration.");

    public override void Write(
        Utf8JsonWriter writer,
        TimeSpan value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(IsoDuration.Format(value));
}
