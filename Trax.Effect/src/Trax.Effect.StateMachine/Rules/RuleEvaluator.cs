using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Evaluates a declarative <see cref="Rule"/> against a context and input. Total: a missing or wrong-typed
/// field makes the predicate false, never throws. A <see cref="Rule.Custom"/> resolves through a supplied
/// handler map; an unregistered custom name is <c>false</c> (the server's authoritative reject), never a
/// crash.
/// </summary>
public static class RuleEvaluator
{
    public static bool Evaluate(
        Rule rule,
        JsonObject context,
        JsonNode? input,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, bool>>? customGuards = null
    ) =>
        rule switch
        {
            Rule.Present r => !IsNullOrMissing(Read(r.Source, r.Field, context, input)),
            Rule.Absent r => IsNullOrMissing(Read(r.Source, r.Field, context, input)),
            Rule.OfType r => MatchesType(Read(r.Source, r.Field, context, input), r.Type),
            Rule.NonEmpty r => IsNonEmpty(Read(r.Source, r.Field, context, input)),
            Rule.OneOf r => IsOneOf(Read(r.Source, r.Field, context, input), r.Values),
            Rule.Compare r => TryNumber(Read(r.Source, r.Field, context, input), out var d)
                && Compare(d, r.Op, r.Value),
            Rule.Count r => Read(r.Source, r.Field, context, input) is JsonArray a
                && Compare(a.Count, r.Op, r.Value),
            Rule.Length r => Read(r.Source, r.Field, context, input) is { } n
                && n.GetValueKind() == JsonValueKind.String
                && Compare(n.GetValue<string>().Length, r.Op, r.Value),
            Rule.BoolEquals r => Read(r.Source, r.Field, context, input) is { } b
                && b.GetValueKind() == (r.Value ? JsonValueKind.True : JsonValueKind.False),
            Rule.ArrayOf r => Read(r.Source, r.Field, context, input) is JsonArray arr
                && arr.All(e => MatchesType(e, r.ElementType)),
            Rule.All r => r.Rules.All(x => Evaluate(x, context, input, customGuards)),
            Rule.Any r => r.Rules.Any(x => Evaluate(x, context, input, customGuards)),
            Rule.Custom r => customGuards is not null
                && customGuards.TryGetValue(r.Name, out var g)
                && g(context, input),
            _ => false,
        };

    private static JsonNode? Read(
        RuleSource source,
        string field,
        JsonObject context,
        JsonNode? input
    )
    {
        var obj = source == RuleSource.Context ? context : input as JsonObject;
        return obj is not null && obj.TryGetPropertyValue(field, out var node) ? node : null;
    }

    private static bool IsNullOrMissing(JsonNode? node) =>
        node is null || node.GetValueKind() == JsonValueKind.Null;

    private static bool MatchesType(JsonNode? node, JsonFieldType type) =>
        node is not null
        && node.GetValueKind() switch
        {
            JsonValueKind.String => type == JsonFieldType.String,
            JsonValueKind.Number => type == JsonFieldType.Number,
            JsonValueKind.True or JsonValueKind.False => type == JsonFieldType.Boolean,
            _ when node is JsonArray => type == JsonFieldType.Array,
            _ when node is JsonObject => type == JsonFieldType.Object,
            _ => false,
        };

    private static bool IsNonEmpty(JsonNode? node) =>
        node switch
        {
            JsonArray a => a.Count > 0,
            not null when node.GetValueKind() == JsonValueKind.String =>
                node.GetValue<string>().Length > 0,
            _ => false,
        };

    private static bool IsOneOf(JsonNode? node, IReadOnlyList<string> values) =>
        node is not null
        && node.GetValueKind() == JsonValueKind.String
        && values.Contains(node.GetValue<string>(), StringComparer.Ordinal);

    // Numbers are read through the raw JSON text and parsed as double, matching the JSON-number-is-a-double
    // model the rest of the engine uses; this tolerates int/long/double/decimal backings without throwing.
    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is null || node.GetValueKind() != JsonValueKind.Number)
            return false;
        return double.TryParse(
            node.ToJsonString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    private static bool Compare(double actual, CompareOp op, double target) =>
        op switch
        {
            CompareOp.GreaterThan => actual > target,
            CompareOp.GreaterOrEqual => actual >= target,
            CompareOp.LessThan => actual < target,
            CompareOp.LessOrEqual => actual <= target,
            CompareOp.EqualTo => actual == target,
            _ => false,
        };
}
