using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
    )
    {
        var read = JsonSerializer.Deserialize(ref reader, TypeInfo(options));
        // A null token is a value that was sent, and an Optional<string?> holds it. Deserialize types its result T?,
        // which for an unconstrained T is T itself with null allowed, so this reinterprets nothing.
        return new(Unsafe.As<T?, T>(ref read));
    }

    public override void Write(
        Utf8JsonWriter writer,
        Optional<T> value,
        JsonSerializerOptions options
    ) => JsonSerializer.Serialize(writer, value.Value, TypeInfo(options));

    private static JsonTypeInfo<T> TypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
