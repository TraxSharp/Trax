namespace Trax.Effect.Utils;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Writes any <see cref="IDisposable"/> as the string <c>"[ IDisposable ]"</c> instead of serializing its members,
/// so a junction output holding a connection or stream can still be logged. Part of
/// <see cref="TraxJsonSerializationOptions.JunctionLogging"/>; infrastructure not intended for direct use.
/// </summary>
internal sealed class DisposableConverter : JsonConverter<object>
{
    /// <summary>
    /// <c>true</c> for any type assignable to <see cref="IDisposable"/>.
    /// </summary>
    /// <param name="typeToConvert">The type being serialized.</param>
    public override bool CanConvert(Type typeToConvert) =>
        typeof(IDisposable).IsAssignableFrom(typeToConvert);

    /// <summary>
    /// Always throws <see cref="NotSupportedException"/>: the placeholder cannot be turned back into an object.
    /// </summary>
    /// <param name="reader">Unused.</param>
    /// <param name="typeToConvert">Unused.</param>
    /// <param name="options">Unused.</param>
    public override object Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => throw new NotSupportedException();

    /// <summary>
    /// Writes the string <c>"[ IDisposable ]"</c> in place of <paramref name="value"/>.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The disposable value; not inspected.</param>
    /// <param name="options">Unused.</param>
    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        writer.WriteStringValue("[ IDisposable ]");
    }
}
