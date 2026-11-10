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
        new(Document, $"{Pointer}/{key.Replace("~", "~0").Replace("/", "~1")}");

    internal At this[int index] => new(Document, $"{Pointer}/{index}");

    internal FormatException Fail(string problem) => new PointedFormatException(this, problem);

    internal JsonObject Object(JsonNode? node) =>
        node as JsonObject ?? throw Fail("must be an object.");

    internal JsonArray Array(JsonNode? node, int minItems = 0)
    {
        var array = node as JsonArray ?? throw Fail("must be a list.");
        if (array.Count < minItems)
            throw Fail($"must hold at least {minItems}.");
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
            throw Fail($"must be {minLength} to {maxLength} characters long.");
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

    /// <summary>A key whose only allowed value is <c>true</c>.</summary>
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

/// <summary>
/// A refusal of a strict document, with where it is (<see cref="At"/>'s pointer) and what is wrong, apart, so a reader
/// of a source the document was elaborated from (a lexicon) can name its own line instead of the key.
/// </summary>
internal sealed class PointedFormatException(At at, string problem)
    : FormatException($"{at.Document} {(at.Pointer.Length == 0 ? "/" : at.Pointer)}: {problem}")
{
    internal string Pointer { get; } = at.Pointer;

    internal string Problem { get; } = problem;
}

/// <summary>The names the formats share, as the schemas spell them, anchored at both ends of the text.</summary>
internal static partial class Names
{
    [GeneratedRegex(@"\A[a-z][a-z0-9]*(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    internal static partial Regex MachineId();

    [GeneratedRegex(@"\A[A-Z][A-Za-z0-9]*\z", RegexOptions.CultureInvariant)]
    internal static partial Regex Member();

    [GeneratedRegex(@"\A[a-z][a-zA-Z0-9]*\z", RegexOptions.CultureInvariant)]
    internal static partial Regex Word();

    [GeneratedRegex(
        @"\A[a-z][a-z0-9]*(-[a-z0-9]+)*/[a-z0-9]+(-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant
    )]
    internal static partial Regex SystemKey();

    [GeneratedRegex(
        @"\A[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*\z",
        RegexOptions.CultureInvariant
    )]
    internal static partial Regex TrainName();

    [GeneratedRegex(
        @"\A([a-z][a-z0-9]*(?:-[a-z0-9]+)*)\.([A-Za-z_][A-Za-z0-9_]*)\z",
        RegexOptions.CultureInvariant
    )]
    internal static partial Regex ViewSource();

    [GeneratedRegex(
        @"(\A|\.)(__proto__|constructor|prototype)(\.|\z)",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex PrototypeName();

    internal static string MachineIdAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 64) is var id && MachineId().IsMatch(id)
            ? id
            : throw at.Fail("is not a machine id: lowercase words joined by single hyphens.");

    internal static string MemberAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 64) is var name && Member().IsMatch(name)
            ? name
            : throw at.Fail("is not a state or trigger name: a capital, then letters and digits.");

    internal static string WordAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 64) is var word && Word().IsMatch(word)
            ? word
            : throw at.Fail("is not a name: a lowercase letter, then letters and digits.");

    internal static string SystemKeyAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 129) is var key && SystemKey().IsMatch(key)
            ? key
            : throw at.Fail("is not a system key: the machine id, a slash and a name.");

    internal static string TrainAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 512) is var train && TrainName().IsMatch(train)
            ? train
            : throw at.Fail("is not a train's full name.");

    /// <summary>The IR's field name: any non-empty text but a path through a prototype name.</summary>
    internal static string FieldNameAt(At at, JsonNode? node) =>
        at.Text(node, minLength: 1) is var name && !PrototypeName().IsMatch(name)
            ? name
            : throw at.Fail("is not a field name any engine holds.");

    /// <summary>
    /// A phrase: 1 to 200 characters, starting and ending with something that is not whitespace, and on one line.
    /// Whitespace and line breaks are what the schema's <c>\S</c> and <c>.</c> mean in ECMAScript, which is how its
    /// validator reads them.
    /// </summary>
    internal static string PhraseAt(At at, JsonNode? node)
    {
        var phrase = at.Text(node, 1, 200);
        if (IsSpace(phrase[0]) || IsSpace(phrase[^1]))
            throw at.Fail("must not start or end with whitespace.");
        foreach (var c in phrase)
            if (c is '\n' or '\r' or '\u2028' or '\u2029')
                throw at.Fail("must be on one line.");
        return phrase;
    }

    /// <summary>
    /// The phrase as it is compared: case folded and every run of whitespace one space, so two phrases a reader
    /// would take for the same step are the same.
    /// </summary>
    internal static string Normal(string phrase)
    {
        var builder = new StringBuilder(phrase.Length);
        var space = false;
        foreach (var c in phrase.ToLowerInvariant())
        {
            if (IsSpace(c))
            {
                space = true;
                continue;
            }
            if (space && builder.Length > 0)
                builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    // ECMAScript's WhiteSpace and LineTerminator, which \s matches.
    private static bool IsSpace(char c) =>
        c
            is '\t'
                or '\n'
                or '\v'
                or '\f'
                or '\r'
                or ' '
                or '\u00a0'
                or '\u1680'
                or (>= '\u2000' and <= '\u200a')
                or '\u2028'
                or '\u2029'
                or '\u202f'
                or '\u205f'
                or '\u3000'
                or '\ufeff';
}
