using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

/// <summary>
/// A variable or input field that may be left out, which GraphQL tells apart from null: left out, the host uses the
/// argument's default or leaves the value as it is; null clears it. <see langword="default"/> is left out; a value, null
/// included, converts to one that is sent (<c>MaxActiveJobs = null</c> sends <c>"maxActiveJobs": null</c>).
/// </summary>
/// <remarks>
/// <see cref="TraxClient"/> leaves a left-out property out of the JSON, with <see cref="TraxJson.Options"/> or with
/// any options given in <see cref="TraxClientOptions.Json"/>. Serialized some other way, with options the client never
/// saw, it is written as <c>default(T)</c>; read, a property that is absent stays left out and one that is null is a
/// null that was sent.
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

    /// <summary>The value, or <see langword="default"/> when it is left out.</summary>
    public T Value { get; }

    object? IOptional.Boxed => Value;

    JsonConverter IOptional.Converter() => new OptionalJsonConverter<T>();

    /// <summary>A value that is sent.</summary>
    public static implicit operator Optional<T>(T value) => new(value);

    /// <summary>The value, or "(left out)".</summary>
    public override string ToString() => HasValue ? Value?.ToString() ?? "null" : "(left out)";
}
