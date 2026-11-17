using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>Writes a <see cref="RefusalCode"/> as its wire spelling, and reads it back (or its older kebab-case spelling).</summary>
public sealed class RefusalCodeJsonConverter : JsonConverter<RefusalCode>
{
    /// <inheritdoc />
    public override RefusalCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        RefusalCodes.TryParse(reader.GetString(), out var code)
            ? code
            : throw new JsonException($"'{reader.GetString()}' is not a refusal code.");

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        RefusalCode value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.ToWire());
}
