using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainExecution;

/// <summary>
/// Rewrites a saved train input that carries System.Text.Json reference metadata as the plain
/// JSON tree it stands for, so <see cref="TrainInputReader.Read"/> reads it as the input the run
/// was given. See <see cref="TrainInputReader.ResolveSavedInput"/>.
/// </summary>
internal static class SavedInputReferences
{
    private const string IdProperty = "$id";
    private const string RefProperty = "$ref";
    private const string ValuesProperty = "$values";

    /// <summary>
    /// How deep a saved input may nest, counting the <c>$values</c> wrappers that reference
    /// metadata adds and the trees a <c>$ref</c> is replaced with. The input type's own depth limit
    /// is applied again when the rewritten JSON is read.
    /// </summary>
    private const int MaxDepth = 64;

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaxDepth };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        MaxDepth = MaxDepth,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Whether <paramref name="json"/> is in the form a writer that preserves references gives
    /// its root: an object whose first property is <c>$id</c>.
    /// </summary>
    public static bool Present(string json)
    {
        var reader = new Utf8JsonReader(
            Encoding.UTF8.GetBytes(json),
            new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip }
        );

        try
        {
            return reader.Read()
                && reader.TokenType == JsonTokenType.StartObject
                && reader.Read()
                && reader.TokenType == JsonTokenType.PropertyName
                && reader.ValueTextEquals(IdProperty);
        }
        catch (JsonException)
        {
            // Not JSON at all: left for the reader to refuse with its own message.
            return false;
        }
    }

    /// <summary>
    /// The plain JSON <paramref name="json"/> stands for: each <c>$id</c> dropped, each
    /// <c>{"$id":..,"$values":[..]}</c> written as its array, and each <c>{"$ref":..}</c> written
    /// as a full copy of the value it refers to.
    /// </summary>
    /// <exception cref="JsonException">
    /// The metadata is malformed, a <c>$ref</c> names no <c>$id</c>, or a value refers to itself.
    /// </exception>
    /// <exception cref="TrainInputValidationException">
    /// The plain form is larger than <paramref name="maxBytes"/>.
    /// </exception>
    public static string Resolve(string json, TrainRegistration registration, int maxBytes)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);

        var ids = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Collect(document.RootElement, ids);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            var resolver = new Resolver(writer, ids, registration, maxBytes);
            resolver.Write(document.RootElement, depth: 0);
            writer.Flush();
            resolver.CheckSize();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The kinds of object reference metadata can make.</summary>
    private enum Shape
    {
        Plain,
        Identified,
        Collection,
        Reference,
    }

    /// <summary>
    /// What <paramref name="element"/> is, refusing metadata anywhere a reference-preserving
    /// writer would not have put it.
    /// </summary>
    private static Shape ShapeOf(JsonElement element, out string? id)
    {
        id = null;
        var index = 0;
        var count = 0;
        var first = default(JsonProperty);
        var hasValues = false;

        foreach (var property in element.EnumerateObject())
        {
            if (index == 0)
                first = property;
            else if (property.NameEquals(IdProperty) || property.NameEquals(RefProperty))
                throw Malformed($"{property.Name} is not the first property of its object");

            if (property.NameEquals(ValuesProperty))
            {
                if (index != 1)
                    throw Malformed("$values does not follow $id");
                hasValues = true;
            }

            index++;
            count++;
        }

        if (count == 0)
            return Shape.Plain;

        if (first.NameEquals(RefProperty))
        {
            if (count != 1)
                throw Malformed("$ref is not the only property of its object");
            id = IdOf(first);
            return Shape.Reference;
        }

        if (!first.NameEquals(IdProperty))
            return Shape.Plain;

        id = IdOf(first);

        if (!hasValues)
            return Shape.Identified;

        if (count != 2)
            throw Malformed("an object with $values has properties besides $id");

        return Shape.Collection;
    }

    private static string IdOf(JsonProperty property) =>
        property.Value.ValueKind == JsonValueKind.String
            ? property.Value.GetString()!
            : throw Malformed($"{property.Name} is not a string");

    /// <summary>Every value carrying an <c>$id</c>, by that id.</summary>
    private static void Collect(JsonElement element, Dictionary<string, JsonElement> ids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var shape = ShapeOf(element, out var id);
                if (shape is Shape.Identified or Shape.Collection && !ids.TryAdd(id!, element))
                    throw Malformed($"$id \"{id}\" is given to more than one value");

                foreach (var property in element.EnumerateObject())
                    if (shape != Shape.Collection || property.NameEquals(ValuesProperty))
                        Collect(property.Value, ids);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, ids);
                break;
        }
    }

    private static JsonException Malformed(string what) =>
        new($"The saved input's reference metadata is malformed: {what}.");

    private sealed class Resolver(
        Utf8JsonWriter writer,
        Dictionary<string, JsonElement> ids,
        TrainRegistration registration,
        int maxBytes
    )
    {
        /// <summary>The ids of the values being written, outermost first, to catch a cycle.</summary>
        private readonly HashSet<string> _open = new(StringComparer.Ordinal);

        public void Write(JsonElement element, int depth)
        {
            if (depth > MaxDepth)
                throw new JsonException(
                    $"The saved input is nested more than {MaxDepth} levels deep once its "
                        + "references are resolved."
                );

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    WriteObject(element, depth);
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                        Write(item, depth + 1);
                    writer.WriteEndArray();
                    break;

                default:
                    element.WriteTo(writer);
                    break;
            }

            CheckSize();
        }

        private void WriteObject(JsonElement element, int depth)
        {
            var shape = ShapeOf(element, out var id);

            switch (shape)
            {
                case Shape.Reference:
                    if (!ids.TryGetValue(id!, out var target))
                        throw Malformed($"$ref \"{id}\" names no $id");
                    if (_open.Contains(id!))
                        throw new JsonException(
                            $"The saved input refers to a value from inside itself ($ref \"{id}\"), "
                                + "so it has no plain form to read."
                        );
                    // The value the reference names, written in full in its place.
                    Write(target, depth);
                    return;

                case Shape.Collection:
                    _open.Add(id!);
                    Write(element.GetProperty(ValuesProperty), depth);
                    _open.Remove(id!);
                    return;

                case Shape.Identified:
                    _open.Add(id!);
                    break;
            }

            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject())
            {
                if (shape == Shape.Identified && property.NameEquals(IdProperty))
                    continue;

                writer.WritePropertyName(property.Name);
                Write(property.Value, depth + 1);
            }
            writer.WriteEndObject();

            if (shape == Shape.Identified)
                _open.Remove(id!);
        }

        /// <summary>
        /// Refuses the plain form the moment it is larger than the cap, so a small saved input
        /// whose references copy one value many times over is never written out in full.
        /// </summary>
        public void CheckSize()
        {
            var written = writer.BytesCommitted + writer.BytesPending;
            if (written > maxBytes)
                throw new TrainInputValidationException(
                    registration.ServiceTypeName,
                    (int)Math.Min(written, int.MaxValue),
                    maxBytes
                );
        }
    }
}
