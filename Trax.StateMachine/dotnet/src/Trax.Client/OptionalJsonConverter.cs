using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

internal sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    // Called for a null token too, so a null read is a value that was sent.
    public override bool HandleNull => true;

    public override Optional<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => new(JsonSerializer.Deserialize(ref reader, TypeInfo(options))!);

    public override void Write(
        Utf8JsonWriter writer,
        Optional<T> value,
        JsonSerializerOptions options
    ) => JsonSerializer.Serialize(writer, value.Value, TypeInfo(options));

    private static JsonTypeInfo<T> TypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
