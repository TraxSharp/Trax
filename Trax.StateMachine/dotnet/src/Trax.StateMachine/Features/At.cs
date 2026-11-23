using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// A position in a document being read strictly: the document's name and a JSON pointer into it. Every check the
/// glossary and host file readers make goes through one of these, so every refusal is a <see cref="FormatException"/>
/// naming the document and the key, and the checks read as the schema does (<c>conformance/glossary.schema.json</c>,
/// <c>conformance/host.schema.json</c>), which the corpus under <c>conformance/formats/</c> holds them to.
/// </summary>
internal readonly record struct At(string Document, string Pointer)
{
    /// <summary>The deepest a document may nest, arrays and objects counted: System.Text.Json's default.</summary>
    internal const int MaxDepth = 64;

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowDuplicateProperties = false,
        MaxDepth = MaxDepth,
    };

    /// <summary>Parses <paramref name="text"/> as one JSON object, refusing a key given twice in one object.</summary>
    internal static JsonObject Parse(string text, string document)
    {
        ArgumentNullException.ThrowIfNull(text);
        var at = new At(document, "");
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, documentOptions: Options);
        }
        catch (JsonException ex)
        {
            throw at.Fail($"is not JSON: {ex.Message}");
        }
        catch (InvalidOperationException)
        {
            // The check for a key given twice decodes every key, and throws on one that is not text.
            throw at.Fail("has a key that is not valid UTF-16 text.");
        }
        var root = at.Object(node);
        Decoded(at, root);
        return root;
    }

    // JsonNode.Parse decodes a string only when it is read, and an escaped lone surrogate (\ud800) then throws
    // InvalidOperationException. Every one is read here first, so it is refused as a FormatException naming where it
    // is, as every other refusal is. Keys are decoded by Parse, which checks them for duplicates.
    private static void Decoded(At at, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject value:
                foreach (var (key, child) in value)
                    Decoded(at[key], child);
                break;
            case JsonArray list:
                for (var i = 0; i < list.Count; i++)
                    Decoded(at[i], list[i]);
                break;
            case JsonValue text when text.GetValueKind() == JsonValueKind.String:
                try
                {
                    _ = text.GetValue<string>();
                }
                catch (InvalidOperationException)
                {
                    throw at.Fail("is not valid UTF-16 text.");
                }
                break;
        }
    }

    internal At this[string key] =>
        new(
            Document,
            $"{Pointer}/{key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}"
        );

    internal At this[int index] =>
        new(Document, string.Create(CultureInfo.InvariantCulture, $"{Pointer}/{index}"));

    internal FormatException Fail(string problem) => new PointedFormatException(this, problem);

    internal JsonObject Object(JsonNode? node) =>
        node as JsonObject ?? throw Fail("must be an object.");

    internal JsonArray Array(JsonNode? node, int minItems = 0)
    {
        var array = node as JsonArray ?? throw Fail("must be a list.");
        if (array.Count < minItems)
            throw Fail(
                string.Create(CultureInfo.InvariantCulture, $"must hold at least {minItems}.")
            );
        return array;
    }

    /// <summary>Refuses any key of <paramref name="value"/> that is not one of <paramref name="known"/>.</summary>
    internal void Closed(JsonObject value, params string[] known)
    {
        foreach (var (key, _) in value)
            if (System.Array.IndexOf(known, key) < 0)
                throw this[key].Fail("is not a key this format has.");
    }

    /// <summary>
    /// The value at <paramref name="key"/>, which must be there. A null is passed on, and refused by whichever check
    /// reads it, as the schema refuses it: no key in these formats may be null.
    /// </summary>
    internal JsonNode? Required(JsonObject value, string key) =>
        value.TryGetPropertyValue(key, out var node) ? node : throw Fail($"must have '{key}'.");

    internal string Text(JsonNode? node, int minLength = 0, int maxLength = int.MaxValue)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
            throw Fail("must be a string.");
        var text = value.GetValue<string>();
        // A length is counted in code points, as the schemas' validator counts it.
        var length = CodePoints(text);
        if (length < minLength || length > maxLength)
            throw Fail(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"must be {minLength} to {maxLength} characters long."
                )
            );
        return text;
    }

    internal string Matching(JsonNode? node, Regex pattern, string what)
    {
        var text = Text(node);
        if (!pattern.IsMatch(text))
            throw Fail($"is not {what}.");
        return text;
    }

    internal double Number(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
            throw Fail("must be a number.");
        var number = value.GetValue<double>();
        if (!double.IsFinite(number))
            throw Fail("must be a finite number.");
        return number;
    }

    internal double Integer(JsonNode? node)
    {
        var number = Number(node);
        if (!double.IsInteger(number))
            throw Fail("must be a whole number.");
        return number;
    }

    internal bool Boolean(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? value.GetValue<bool>()
            : throw Fail("must be true or false.");

    /// <summary>A key whose only allowed value is <see langword="true"/>.</summary>
    internal void True(JsonNode? node)
    {
        if (node?.GetValueKind() != JsonValueKind.True)
            throw Fail("must be true.");
    }

    internal string OneOf(JsonNode? node, params string[] allowed)
    {
        var text = Text(node);
        if (System.Array.IndexOf(allowed, text) < 0)
            throw Fail($"must be one of {string.Join(", ", allowed)}.");
        return text;
    }

    /// <summary>A list of strings, refusing a repeated one.</summary>
    internal List<string> Distinct(JsonNode? node, int minItems, Func<At, JsonNode?, string> item)
    {
        var array = Array(node, minItems);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<string>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var text = item(this[i], array[i]);
            if (!seen.Add(text))
                throw this[i].Fail($"repeats '{text}'.");
            items.Add(text);
        }
        return items;
    }

    private static int CodePoints(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
            count++;
        return count;
    }
}
