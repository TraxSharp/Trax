using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Sets up a context in which a guard holds or fails, for <c>Given Ada's order has nothing in it</c>. Only a guard on one
/// context field can be set up: a guard on the step's input is said in the When, and one combining rules or bound by
/// name is set up through the Whens that reach it.
/// </summary>
internal static class Satisfy
{
    internal static JsonObject? Context(
        GlossaryMachine machine,
        string guardName,
        bool holds,
        JsonObject context,
        out string? why
    )
    {
        why = null;
        var rule = IrMachine.ReadRule(machine.Guards[guardName].Rule);
        var copy = (JsonObject)context.DeepClone();
        var phrase =
            (holds ? machine.Guards[guardName].Holds : machine.Guards[guardName].Fails)
            ?? guardName;
        var field = ContextField(rule);
        if (field is null)
        {
            why = rule is Rule.All or Rule.Any or Rule.Custom
                ? $"'{phrase}' cannot be set up by a Given: its guard combines rules. Reach it through the Whens that make it so."
                : $"'{phrase}' reads the step's input, not the context, so it is said in the When, not a Given.";
            return null;
        }
        var type = machine.FieldType(field);
        if (type is null)
        {
            why =
                $"'{phrase}' reads '{field}', which is not one of the {machine.Reference}'s context fields.";
            return null;
        }

        var value = Value(rule, holds, type);
        copy[field] = value;
        if (RuleEvaluator.Evaluate(rule, copy, input: null) != holds)
        {
            why = $"No value of '{field}' makes '{phrase}' {(holds ? "hold" : "fail")}.";
            return null;
        }
        return copy;
    }

    // The one context field a guard reads, or null when it reads the input or combines rules.
    private static string? ContextField(Rule rule) =>
        rule switch
        {
            Rule.Present r when r.Source == RuleSource.Context => r.Field,
            Rule.Absent r when r.Source == RuleSource.Context => r.Field,
            Rule.NonEmpty r when r.Source == RuleSource.Context => r.Field,
            Rule.Compare r when r.Source == RuleSource.Context => r.Field,
            Rule.BoolEquals r when r.Source == RuleSource.Context => r.Field,
            Rule.Count r when r.Source == RuleSource.Context => r.Field,
            Rule.Length r when r.Source == RuleSource.Context => r.Field,
            Rule.OneOf r when r.Source == RuleSource.Context => r.Field,
            _ => null,
        };

    // A value of the field's type that makes the rule hold or fail.
    private static JsonNode? Value(Rule rule, bool holds, string type) =>
        (rule, holds) switch
        {
            (Rule.Present, true) or (Rule.Absent, false) or (Rule.NonEmpty, true) => Sample(type),
            (Rule.Present, false) or (Rule.Absent, true) => null,
            (Rule.NonEmpty, false) => Empty(type),
            (Rule.Compare c, _) => Number(type, Side(c.Op, c.Value, holds)),
            (Rule.BoolEquals b, _) => JsonValue.Create(b.Value == holds),
            (Rule.Count c, _) => new JsonArray(
                Enumerable
                    .Range(0, (int)Math.Max(0, Side(c.Op, c.Value, holds)))
                    .Select(_ => (JsonNode?)Sample("string"))
                    .ToArray()
            ),
            (Rule.Length c, _) => JsonValue.Create(
                new string('x', (int)Math.Max(0, Side(c.Op, c.Value, holds)))
            ),
            (Rule.OneOf o, true) when o.Values.Count > 0 => JsonValue.Create(o.Values[0]),
            (Rule.OneOf o, _) => JsonValue.Create(string.Concat(o.Values) + "-other"),
            _ => null,
        };

    // A number on the side of the comparison the guard should land on.
    private static double Side(CompareOp op, double value, bool holds) =>
        (op, holds) switch
        {
            (CompareOp.GreaterThan, true) or (CompareOp.LessOrEqual, false) => Math.Floor(value)
                + 1,
            (CompareOp.GreaterOrEqual, true) or (CompareOp.LessThan, false) => Math.Ceiling(value),
            (CompareOp.LessThan, true) or (CompareOp.GreaterOrEqual, false) => Math.Ceiling(value)
                - 1,
            (CompareOp.LessOrEqual, true) or (CompareOp.GreaterThan, false) => Math.Floor(value),
            (CompareOp.EqualTo, true) => value,
            _ => value + 1,
        };

    private static JsonNode Number(string type, double value) =>
        string.Equals(type, "integer", StringComparison.Ordinal)
            ? JsonValue.Create((long)value)
            : JsonValue.Create(value);

    private static JsonNode? Sample(string type) =>
        type switch
        {
            "string" => JsonValue.Create("x"),
            "integer" or "number" => JsonValue.Create(1),
            "boolean" => JsonValue.Create(value: true),
            "array" => new JsonArray(JsonValue.Create("x")),
            _ => new JsonObject { ["x"] = "x" },
        };

    private static JsonNode? Empty(string type) =>
        type switch
        {
            "string" => JsonValue.Create(""),
            "array" => new JsonArray(),
            "object" => new JsonObject(),
            _ => null,
        };
}
