using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// The IR's field, rule and reduction shapes, checked as strictly as <c>conformance/ir.schema.json</c>'s
/// <c>$defs</c> say (<c>field</c>, <c>valueShape</c>, <c>keyShape</c>, <c>rule</c>, <c>reduction</c>). The glossary
/// borrows them rather than having a second vocabulary for "non-empty", so a glossary is refused for what an IR would
/// be refused for. <see cref="IrMachine"/> reads the same shapes leniently, after the exporter wrote them.
/// </summary>
internal static class IrShapes
{
    private static readonly string[] Types =
    [
        "string",
        "number",
        "integer",
        "boolean",
        "array",
        "object",
    ];

    private static readonly string[] Formats =
    [
        "uuid",
        "date-time",
        "date-time-offset",
        "date",
        "time",
        "duration",
        "uri",
        "char",
    ];

    private static readonly string[] Sources = ["context", "input"];
    private static readonly string[] Ops = ["gt", "gte", "lt", "lte", "eq"];

    /// <summary>A list of fields; returns a copy, and the names in it.</summary>
    internal static (JsonArray Fields, List<string> Names) Fields(At at, JsonNode? node)
    {
        var array = at.Array(node);
        var names = new List<string>(array.Count);
        for (var i = 0; i < array.Count; i++)
            names.Add(Field(at[i], array[i]));
        return ((JsonArray)array.DeepClone(), names);
    }

    /// <summary>Checks one field and returns its name.</summary>
    internal static string Field(At at, JsonNode? node)
    {
        var field = at.Object(node);
        at.Closed(field, [.. ShapeKeys, "name", "constraints"]);
        var name = Names.FieldNameAt(at["name"], at.Required(field, "name"));
        Shape(at, field);
        at["nullable"].Boolean(at.Required(field, "nullable"));
        var constraints = at["constraints"].Array(at.Required(field, "constraints"));
        for (var i = 0; i < constraints.Count; i++)
            Rule(at["constraints"][i], constraints[i]);
        return name;
    }

    private static readonly string[] ShapeKeys =
    [
        "type",
        "nullable",
        "minimum",
        "maximum",
        "enum",
        "enumExact",
        "format",
        "fields",
        "values",
        "keys",
    ];

    // What a field, a valueShape and a keyShape have in common: all but nullable, which each checks itself.
    private static void Shape(At at, JsonObject shape)
    {
        at["type"].OneOf(at.Required(shape, "type"), Types);
        if (shape.ContainsKey("minimum"))
            at["minimum"].Number(shape["minimum"]);
        if (shape.ContainsKey("maximum"))
            at["maximum"].Number(shape["maximum"]);
        foreach (var key in new[] { "enum", "enumExact" })
            if (shape.ContainsKey(key))
            {
                var values = at[key].Array(shape[key]);
                for (var i = 0; i < values.Count; i++)
                    at[key][i].Text(values[i]);
            }
        if (shape.ContainsKey("format"))
            at["format"].OneOf(shape["format"], Formats);
        if (shape.ContainsKey("fields"))
        {
            var fields = at["fields"].Array(shape["fields"]);
            for (var i = 0; i < fields.Count; i++)
                Field(at["fields"][i], fields[i]);
        }
        if (shape.ContainsKey("values"))
            ValueShape(at["values"], shape["values"], key: false);
        if (shape.ContainsKey("keys"))
            ValueShape(at["keys"], shape["keys"], key: true);
    }

    private static void ValueShape(At at, JsonNode? node, bool key)
    {
        var shape = at.Object(node);
        at.Closed(shape, ShapeKeys);
        Shape(at, shape);
        var nullable = at["nullable"].Boolean(at.Required(shape, "nullable"));
        if (key && nullable)
            throw at["nullable"].Fail("must be false: a key is never null.");
    }

    /// <summary>Checks one rule; returns a copy.</summary>
    internal static JsonObject Rule(At at, JsonNode? node)
    {
        var rule = at.Object(node);
        var kind = at["rule"]
            .OneOf(
                at.Required(rule, "rule"),
                "present",
                "absent",
                "nonEmpty",
                "ofType",
                "arrayOf",
                "oneOf",
                "compare",
                "count",
                "length",
                "boolEquals",
                "all",
                "any",
                "custom"
            );
        switch (kind)
        {
            case "all" or "any":
                at.Closed(rule, "rule", "rules");
                var rules = at["rules"].Array(at.Required(rule, "rules"));
                for (var i = 0; i < rules.Count; i++)
                    Rule(at["rules"][i], rules[i]);
                break;
            case "custom":
                at.Closed(rule, "rule", "name");
                at["name"].Text(at.Required(rule, "name"), minLength: 1);
                break;
            default:
                Leaf(at, rule, kind);
                break;
        }
        return (JsonObject)rule.DeepClone();
    }

    private static void Leaf(At at, JsonObject rule, string kind)
    {
        string[] extra = kind switch
        {
            "ofType" or "arrayOf" => ["type"],
            "oneOf" => ["values"],
            "compare" or "count" or "length" => ["op", "value"],
            "boolEquals" => ["value"],
            _ => [],
        };
        at.Closed(rule, ["rule", "source", "field", .. extra]);
        at["source"].OneOf(at.Required(rule, "source"), Sources);
        Names.FieldNameAt(at["field"], at.Required(rule, "field"));
        switch (kind)
        {
            case "ofType" or "arrayOf":
                at["type"].OneOf(at.Required(rule, "type"), Types);
                break;
            case "oneOf":
                var values = at["values"].Array(at.Required(rule, "values"));
                for (var i = 0; i < values.Count; i++)
                    at["values"][i].Text(values[i]);
                break;
            case "compare":
                at["op"].OneOf(at.Required(rule, "op"), Ops);
                at["value"].Number(at.Required(rule, "value"));
                break;
            case "count" or "length":
                at["op"].OneOf(at.Required(rule, "op"), Ops);
                at["value"].Integer(at.Required(rule, "value"));
                break;
            case "boolEquals":
                at["value"].Boolean(at.Required(rule, "value"));
                break;
        }
    }

    /// <summary>Checks one reduction; returns a copy.</summary>
    internal static JsonObject Reduction(At at, JsonNode? node)
    {
        var reduction = at.Object(node);
        var kind = at["reduce"]
            .OneOf(at.Required(reduction, "reduce"), "keep", "clear", "reset", "custom", "set");
        switch (kind)
        {
            case "custom":
                at.Closed(reduction, "reduce", "name");
                at["name"].Text(at.Required(reduction, "name"), minLength: 1);
                break;
            case "set":
                at.Closed(reduction, "reduce", "steps");
                var steps = at["steps"].Array(at.Required(reduction, "steps"));
                for (var i = 0; i < steps.Count; i++)
                    Step(at["steps"][i], steps[i]);
                break;
            default:
                at.Closed(reduction, "reduce");
                break;
        }
        return (JsonObject)reduction.DeepClone();
    }

    private static void Step(At at, JsonNode? node)
    {
        var step = at.Object(node);
        at.Closed(step, "field", "value");
        Names.FieldNameAt(at["field"], at.Required(step, "field"));
        var value = at["value"].Object(at.Required(step, "value"));
        // One of { "input": field } or { "const": anything }, never both and never neither.
        if (value.Count != 1 || !(value.ContainsKey("input") || value.ContainsKey("const")))
            throw at["value"].Fail("must be { \"input\": field } or { \"const\": value }.");
        if (value.ContainsKey("input"))
            Names.FieldNameAt(at["value"]["input"], value["input"]);
    }
}
