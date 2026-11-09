using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>
/// Writes a glossary in its one canonical form, so a glossary elaborated from a lexicon can be committed and compared
/// byte for byte: each object's keys in the order <c>conformance/glossary.schema.json</c> lists them (and the IR
/// schema, for fields, rules and reductions), every map in the order it was declared, two spaces of indent, a
/// trailing newline, and nothing escaped that JSON does not require (an apostrophe and non-ASCII text are written as
/// they are).
/// </summary>
internal static class GlossaryWriter
{
    private static readonly Dictionary<string, string[]> Order = new(StringComparer.Ordinal)
    {
        ["root"] = ["version", "actors", "machines", "views"],
        ["actor"] = ["noun", "roles", "anyAuthenticated"],
        ["machine"] =
        [
            "ownedBy",
            "system",
            "noun",
            "name",
            "version",
            "start",
            "initial",
            "context",
            "initialContext",
            "states",
            "triggers",
            "guards",
            "invokes",
            "links",
            "differential",
            "rules",
            "reductions",
            "migrations",
        ],
        ["state"] = ["phrase", "committed", "requires", "context"],
        ["trigger"] = ["phrase", "input", "reduce", "effect", "guard"],
        ["effect"] = ["type", "keyPrefix", "example"],
        ["handler"] = ["type", "example"],
        ["guard"] = ["holds", "fails", "rule"],
        ["invoke"] = ["train", "input", "output", "outcomes", "examples"],
        ["outcome"] = ["outcome", "when", "reduce"],
        ["link"] = ["machine", "field"],
        ["view"] = ["reads", "fields"],
        ["read"] = ["machine", "key"],
        ["viewField"] = ["from", "labels"],
        ["field"] =
        [
            "name",
            "type",
            "nullable",
            "constraints",
            "minimum",
            "maximum",
            "enum",
            "enumExact",
            "format",
            "fields",
            "values",
            "keys",
        ],
        ["rule"] = ["rule", "source", "field", "type", "values", "op", "value", "rules", "name"],
        ["reduction"] = ["reduce", "steps", "name"],
        ["step"] = ["field", "value"],
        ["system"] = ["keyedBy"],
    };

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The glossary's canonical text.</summary>
    internal static string Write(JsonObject glossary)
    {
        var ordered = Root(glossary);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, Options))
            ordered.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static JsonObject Kind(
        JsonObject node,
        string kind,
        Func<string, JsonNode?, JsonNode?> child
    )
    {
        var order = Order[kind];
        var result = new JsonObject();
        foreach (var key in order)
            if (node.TryGetPropertyValue(key, out var value))
                result[key] = child(key, value);
        // Anything the schema does not list is kept, last, so the reader refuses it rather than this dropping it.
        foreach (var (key, value) in node)
            if (Array.IndexOf(order, key) < 0)
                result[key] = value?.DeepClone();
        return result;
    }

    private static JsonNode? Copy(JsonNode? node) => node?.DeepClone();

    private static JsonObject Map(JsonNode? node, Func<JsonNode?, JsonNode?> each)
    {
        var result = new JsonObject();
        if (node is JsonObject map)
            foreach (var (key, value) in map)
                result[key] = each(value);
        return result;
    }

    private static JsonNode? List(JsonNode? node, Func<JsonNode?, JsonNode?> each) =>
        node is JsonArray list ? new JsonArray(list.Select(each).ToArray()) : Copy(node);

    private static JsonNode? As(JsonNode? node, Func<JsonObject, JsonNode?> each) =>
        node is JsonObject o ? each(o) : Copy(node);

    private static JsonObject Root(JsonObject root) =>
        Kind(
            root,
            "root",
            (key, value) =>
                key switch
                {
                    "actors" => Map(value, a => As(a, o => Kind(o, "actor", (_, v) => Copy(v)))),
                    "machines" => Map(value, m => As(m, Machine)),
                    "views" => Map(value, v => As(v, View)),
                    _ => Copy(value),
                }
        );

    private static JsonNode? Field(JsonNode? node) =>
        As(
            node,
            f =>
                Kind(
                    f,
                    "field",
                    (key, value) =>
                        key switch
                        {
                            "fields" => List(value, Field),
                            "constraints" => List(value, Rule),
                            "values" or "keys" => Field(value),
                            _ => Copy(value),
                        }
                )
        );

    private static JsonNode? Rule(JsonNode? node) =>
        As(
            node,
            r => Kind(r, "rule", (key, value) => key == "rules" ? List(value, Rule) : Copy(value))
        );

    private static JsonNode? Reduction(JsonNode? node) =>
        As(
            node,
            r =>
                Kind(
                    r,
                    "reduction",
                    (key, value) =>
                        key == "steps"
                            ? List(value, s => As(s, o => Kind(o, "step", (_, v) => Copy(v))))
                            : Copy(value)
                )
        );

    private static JsonNode? Machine(JsonObject machine) =>
        Kind(
            machine,
            "machine",
            (key, value) =>
                key switch
                {
                    "system" => As(value, o => Kind(o, "system", (_, v) => Copy(v))),
                    "context" => List(value, Field),
                    "states" => Map(value, s => As(s, State)),
                    "triggers" => Map(value, t => As(t, Trigger)),
                    "guards" => Map(
                        value,
                        g => As(g, o => Kind(o, "guard", (k, v) => k == "rule" ? Rule(v) : Copy(v)))
                    ),
                    "invokes" => Map(value, i => As(i, Invoke)),
                    "links" => Map(value, l => As(l, o => Kind(o, "link", (_, v) => Copy(v)))),
                    "rules" or "reductions" => Map(
                        value,
                        h => As(h, o => Kind(o, "handler", (_, v) => Copy(v)))
                    ),
                    _ => Copy(value),
                }
        );

    private static JsonNode? State(JsonObject state) =>
        Kind(
            state,
            "state",
            (key, value) =>
                key switch
                {
                    "requires" => Rule(value),
                    "context" => List(value, Field),
                    _ => Copy(value),
                }
        );

    private static JsonNode? Trigger(JsonObject trigger) =>
        Kind(
            trigger,
            "trigger",
            (key, value) =>
                key switch
                {
                    "input" => List(value, Field),
                    "reduce" => Reduction(value),
                    "effect" => As(value, o => Kind(o, "effect", (_, v) => Copy(v))),
                    _ => Copy(value),
                }
        );

    private static JsonNode? Invoke(JsonObject invoke) =>
        Kind(
            invoke,
            "invoke",
            (key, value) =>
                key switch
                {
                    "output" => List(value, Field),
                    "outcomes" => Map(
                        value,
                        o =>
                            As(
                                o,
                                x =>
                                    Kind(
                                        x,
                                        "outcome",
                                        (k, v) =>
                                            k switch
                                            {
                                                "when" => Rule(v),
                                                "reduce" => Reduction(v),
                                                _ => Copy(v),
                                            }
                                    )
                            )
                    ),
                    _ => Copy(value),
                }
        );

    private static JsonNode? View(JsonObject view) =>
        Kind(
            view,
            "view",
            (key, value) =>
                key switch
                {
                    "reads" => List(value, r => As(r, o => Kind(o, "read", (_, v) => Copy(v)))),
                    "fields" => Map(
                        value,
                        f => As(f, o => Kind(o, "viewField", (_, v) => Copy(v)))
                    ),
                    _ => Copy(value),
                }
        );
}
