using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Utils;

/// <summary>
/// Reading a stored input or output back, whichever version wrote it.
/// </summary>
/// <remarks>
/// Until reference preservation was dropped from <see cref="TraxJsonSerializationOptions.Default"/>,
/// a stored value carried <c>$id</c> on every object and wrote a list as
/// <c>{"$id":"2","$values":[…]}</c>. Read without reference handling, such a list is an object where
/// an array belongs and fails. A row that carries the metadata is read the way it was written; any
/// other is read as the plain tree it is.
/// </remarks>
internal static class StoredJson
{
    /// <summary>
    /// <paramref name="options"/>, with reference preservation on when <paramref name="stored"/>
    /// carries reference metadata.
    /// </summary>
    public static JsonSerializerOptions ReadOptions(string stored, JsonSerializerOptions options) =>
        CarriesReferences(stored) && options.ReferenceHandler != ReferenceHandler.Preserve
            ? new JsonSerializerOptions(options) { ReferenceHandler = ReferenceHandler.Preserve }
            : options;

    /// <summary>
    /// Whether <paramref name="stored"/> has an <c>$id</c> member on its root object, which every
    /// writer that preserved references gave it. Not JSON, or not an object: false.
    /// </summary>
    public static bool CarriesReferences(string stored)
    {
        try
        {
            using var document = JsonDocument.Parse(stored);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("$id", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
