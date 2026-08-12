using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Validates a context against a <see cref="ContextSchema"/>: every non-nullable field present and correctly
/// typed, no fields outside the schema, and each field's constraints satisfied. Returns <c>null</c> when
/// valid, else a message. The message is non-contract detail (only "valid vs not" is the contract), so it is
/// free to differ from a hand-written validator's wording. This is the schema-derived replacement for a
/// hand-written <c>Holds(...)</c>.
/// </summary>
public static class SchemaValidator
{
    public static string? Validate(ContextSchema schema, JsonObject context)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            known.Add(field.Name);
            var present = context.TryGetPropertyValue(field.Name, out var node);
            var isNull = !present || node is null || node.GetValueKind() == JsonValueKind.Null;

            if (isNull)
            {
                if (!field.Nullable)
                    return $"'{field.Name}' is required.";
                continue;
            }

            if (!MatchesType(node!, field.Type))
                return $"'{field.Name}' must be a {field.Type.ToString().ToLowerInvariant()}.";

            foreach (var constraint in field.Constraints)
                if (!RuleEvaluator.Evaluate(constraint, context, input: null))
                    return $"'{field.Name}' failed a constraint.";
        }

        foreach (var kv in context)
            if (!known.Contains(kv.Key))
                return $"unexpected field '{kv.Key}'.";

        return null;
    }

    private static bool MatchesType(JsonNode node, JsonFieldType type) =>
        node.GetValueKind() switch
        {
            JsonValueKind.String => type == JsonFieldType.String,
            JsonValueKind.Number => type == JsonFieldType.Number,
            JsonValueKind.True or JsonValueKind.False => type == JsonFieldType.Boolean,
            _ when node is JsonArray => type == JsonFieldType.Array,
            _ when node is JsonObject => type == JsonFieldType.Object,
            _ => false,
        };
}
