using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Validates a context, a trigger's input or a train's output against a <see cref="ContextSchema"/>: every
/// non-nullable field present and correctly typed, a number within its CLR type's range, a string one of its enum's
/// names and of its CLR type's format, no fields outside the
/// schema, each field's constraints satisfied, a nested record's fields checked the same way, and every value of
/// a dictionary or element of a list checked against its type, and every key of a dictionary against its key type.
/// Returns <c>null</c> when valid, else a
/// message. The message is non-contract detail (only "valid vs not" is the contract), so it is free to differ
/// from a hand-written validator's wording. This is the schema-derived replacement for a hand-written
/// <c>Holds(...)</c>.
///
/// <para>Infrastructure that <c>Context&lt;T&gt;()</c> and <c>WithInput&lt;T&gt;()</c> compile to; not intended
/// to be called directly.</para>
/// </summary>
internal static partial class SchemaValidator
{
    /// <summary>
    /// Checks <paramref name="context"/> against <paramref name="schema"/>, stopping at the first failure.
    /// A missing field and a JSON null are treated alike: both pass for a nullable field and fail for a
    /// required one.
    /// </summary>
    /// <param name="schema">The declared schema.</param>
    /// <param name="context">The object to check; it is not modified.</param>
    /// <returns>Null when the object is valid; otherwise a short message naming the first failing field.</returns>
    public static string? Validate(ContextSchema schema, JsonObject context) =>
        ValidateObject(schema.Fields, context, "");

    /// <summary>
    /// Checks a trigger's input (or a train's output) against <paramref name="schema"/>. No input (<c>null</c>)
    /// is read as an empty object, so it passes exactly when every field is nullable; anything other than an
    /// object is refused. Never throws: an input that cannot be read (a duplicate key, which a parsed
    /// <see cref="JsonObject"/> throws on when enumerated) is refused like any other bad input.
    /// </summary>
    /// <param name="schema">The input's declared schema; <see cref="ContextSchema.Empty"/> accepts only null or <c>{}</c>.</param>
    /// <param name="input">The input as sent.</param>
    /// <returns>Null when the input is valid; otherwise a short message.</returns>
    public static string? ValidateInput(ContextSchema schema, JsonNode? input)
    {
        try
        {
            if (input is null || input.GetValueKind() == JsonValueKind.Null)
                return ValidateObject(schema.Fields, new JsonObject(), "");
            if (input is not JsonObject obj)
                return "The input must be a JSON object.";
            return ValidateObject(schema.Fields, obj, "");
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            return "The input could not be read.";
        }
    }

    private static string? ValidateObject(
        IReadOnlyList<FieldSchema> fields,
        JsonObject obj,
        string path
    )
    {
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            known.Add(field.Name);
            var name = path + field.Name;
            var present = obj.TryGetPropertyValue(field.Name, out var node);
            var isNull = !present || node is null || node.GetValueKind() == JsonValueKind.Null;

            if (isNull)
            {
                if (!field.Nullable)
                    return $"'{name}' is required.";
                continue;
            }

            if (!Matches(node, field.Type))
                return $"'{name}' must be a {field.Type.ToString().ToLowerInvariant()}.";
            if (!InRange(node!, field))
                return $"'{name}' is out of range.";
            if (Shapeless(node!, field) is { } shapeless)
                return $"'{name}' {shapeless}.";

            // A constraint names a field of the object being validated, whichever side (context or input) it
            // says that object lives on, so both read this object.
            foreach (var constraint in field.Constraints)
                if (!RuleEvaluator.Evaluate(constraint, obj, obj))
                    return $"'{name}' failed a constraint.";

            if (field.Fields is { } shape && ValidateShape(shape, node!, name) is { } nested)
                return nested;
            if (field.Values is { } values && ValidateValues(values, node!, name) is { } value)
                return value;
            if (field.Keys is { } keys && ValidateKeys(keys, node!, name) is { } key)
                return key;
        }

        foreach (var kv in obj)
            if (!known.Contains(kv.Key))
                return $"unexpected field '{path}{kv.Key}'.";

        return null;
    }

    // A nested record's fields, or every element of a list of records.
    private static string? ValidateShape(
        IReadOnlyList<FieldSchema> shape,
        JsonNode node,
        string name
    )
    {
        if (node is JsonObject obj)
            return ValidateObject(shape, obj, name + ".");
        if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject element)
                    return $"'{name}[{i}]' must be an object.";
                if (ValidateObject(shape, element, $"{name}[{i}].") is { } problem)
                    return problem;
            }
        return null;
    }

    // Every value of a dictionary, or every element of a list, against the schema its CLR type gives it.
    private static string? ValidateValues(FieldSchema values, JsonNode node, string name)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
                if (ValidateValue(values, value, $"{name}.{key}") is { } problem)
                    return problem;
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
                if (ValidateValue(values, array[i], $"{name}[{i}]") is { } problem)
                    return problem;
        }
        return null;
    }

    private static string? ValidateValue(FieldSchema schema, JsonNode? node, string name)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
            return schema.Nullable ? null : $"'{name}' is required.";
        if (!Matches(node, schema.Type))
            return $"'{name}' must be a {schema.Type.ToString().ToLowerInvariant()}.";
        if (!InRange(node, schema))
            return $"'{name}' is out of range.";
        if (Shapeless(node, schema) is { } shapeless)
            return $"'{name}' {shapeless}.";
        if (schema.Fields is { } shape && ValidateShape(shape, node, name) is { } nested)
            return nested;
        if (schema.Values is { } values && ValidateValues(values, node, name) is { } value)
            return value;
        if (schema.Keys is { } keys && ValidateKeys(keys, node, name) is { } key)
            return key;
        return null;
    }

    // Every key of a dictionary, against the schema its CLR key type gives it.
    private static string? ValidateKeys(FieldSchema keys, JsonNode node, string name)
    {
        if (node is not JsonObject obj)
            return null;
        foreach (var (key, _) in obj)
        {
            var problem =
                keys.Type == JsonFieldType.Integer
                    ? (IsIntegerKey(key, keys) ? null : "must be an integer in range")
                    : Shapeless(JsonValue.Create(key), keys);
            if (problem is not null)
                return $"'{name}' has a key '{key}' that {problem}.";
        }
        return null;
    }

    // An integer key is its digits as System.Text.Json writes them (no sign but a leading minus, no leading zero),
    // within the key type's range compared exactly: a key is text, never a double.
    private static bool IsIntegerKey(string key, FieldSchema keys)
    {
        if (key.Length > 40 || !IntegerKeyPattern().IsMatch(key))
            return false;
        var value = BigInteger.Parse(
            key,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture
        );
        return (keys.Minimum is not { } min || value >= new BigInteger(min))
            && (keys.Maximum is not { } max || value <= new BigInteger(max));
    }

    [GeneratedRegex("^(?:0|-?[1-9][0-9]*)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerKeyPattern();

    // Why a string is not a value of the field's CLR type: not one of its enum's names, or not of its format.
    // Null when it is, or the field has neither.
    private static string? Shapeless(JsonNode node, FieldSchema field)
    {
        if (field.Enum is null && field.Format is null)
            return null;
        if (node.GetValueKind() != JsonValueKind.String)
            return null;
        var value = node.GetValue<string>();
        if (field.Enum is { } names && !ValueFormats.IsOneOf(value, names, field.EnumExact))
            return "must be one of " + string.Join(", ", names);
        if (field.Format is { } format && !ValueFormats.Matches(format, value))
            return $"must be a {format}";
        return null;
    }

    // A number within the field's range, compared as the double both engines read it as. A field with no range
    // accepts any value of its type.
    private static bool InRange(JsonNode node, FieldSchema field)
    {
        if (field.Minimum is null && field.Maximum is null)
            return true;
        if (node.GetValueKind() != JsonValueKind.Number || !TryFinite(node, out var number))
            return false;
        return (field.Minimum is not { } min || number >= min)
            && (field.Maximum is not { } max || number <= max);
    }

    /// <summary>
    /// Whether <paramref name="node"/> is a value of <paramref name="type"/>, the same in both engines: a number
    /// is finite (<c>1e400</c> is not one), an integer is a number with no fractional part decided by its value
    /// (<c>2020.0</c> is one), and a string is well-formed UTF-16 (no lone surrogate). Never throws.
    /// </summary>
    internal static bool Matches(JsonNode? node, JsonFieldType type)
    {
        if (node is null)
            return false;
        switch (node.GetValueKind())
        {
            case JsonValueKind.String:
                return type == JsonFieldType.String && IsWellFormed(node);
            case JsonValueKind.Number:
                if (!TryFinite(node, out var number))
                    return false;
                return type == JsonFieldType.Number
                    || (type == JsonFieldType.Integer && double.IsInteger(number));
            case JsonValueKind.True or JsonValueKind.False:
                return type == JsonFieldType.Boolean;
            default:
                return node switch
                {
                    JsonArray => type == JsonFieldType.Array,
                    JsonObject => type == JsonFieldType.Object,
                    _ => false,
                };
        }
    }

    // A parsed number is read from its text, so 1e400 comes back as infinity rather than failing to parse.
    private static bool TryFinite(JsonNode node, out double value)
    {
        var json = node.AsValue();
        if (json.TryGetValue(out value))
            return double.IsFinite(value);
        return double.TryParse(
                json.ToJsonString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value
            ) && double.IsFinite(value);
    }

    // A lone surrogate has no UTF-8 encoding, so no store or client could read it back as it was sent. A parsed
    // string holding one throws when it is read, which is the same verdict.
    private static bool IsWellFormed(JsonNode node)
    {
        string value;
        try
        {
            value = node.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return false;
                i++;
            }
            else if (char.IsLowSurrogate(value[i]))
                return false;
        }
        return true;
    }
}
