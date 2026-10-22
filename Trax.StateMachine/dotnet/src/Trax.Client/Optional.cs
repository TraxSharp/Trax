using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Client;

/// <summary>
/// A variable or input field that may be left out, which GraphQL tells apart from null: left out, the host uses the
/// argument's default or leaves the value as it is; null clears it. <c>default</c> is left out; a value, null
/// included, converts to one that is sent (<c>MaxActiveJobs = null</c> sends <c>"maxActiveJobs": null</c>).
/// </summary>
/// <remarks>
/// <see cref="TraxJson.Options"/> leaves a left-out property out of the JSON. With other options it is written as its
/// value, or null when left out.
/// </remarks>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly record struct Optional<T> : IOptional
{
    /// <summary>A value that is sent.</summary>
    public Optional(T value)
    {
        Value = value;
        HasValue = true;
    }

    /// <summary>Whether the value is sent.</summary>
    public bool HasValue { get; }

    /// <summary>The value, or <c>default</c> when it is left out.</summary>
    public T Value { get; }

    object? IOptional.Boxed => Value;

    /// <summary>A value that is sent.</summary>
    public static implicit operator Optional<T>(T value) => new(value);

    /// <summary>The value, or "(left out)".</summary>
    public override string ToString() => HasValue ? Value?.ToString() ?? "null" : "(left out)";
}

/// <summary>What <see cref="TraxJson"/> reads of any <see cref="Optional{T}"/>.</summary>
internal interface IOptional
{
    bool HasValue { get; }
    object? Boxed { get; }
}

internal sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (JsonConverter)
            Activator.CreateInstance(
                typeof(OptionalJsonConverter<>).MakeGenericType(
                    typeToConvert.GetGenericArguments()[0]
                )
            )!;
}

internal sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    // Called for a null token too, so a null read is a value that was sent.
    public override bool HandleNull => true;

    public override Optional<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => new(JsonSerializer.Deserialize<T>(ref reader, options)!);

    public override void Write(
        Utf8JsonWriter writer,
        Optional<T> value,
        JsonSerializerOptions options
    ) => JsonSerializer.Serialize(writer, value.Value, options);
}
