using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// One machine of a glossary, owned by the actors in <see cref="OwnedBy"/> (one or more) or the system's.
/// <see cref="Reference"/> is a user's machine's noun (<c>order</c>, said as
/// <c>Ada's order</c>) or a system machine's name (<c>the shop</c>).
/// </summary>
internal sealed record GlossaryMachine(
    string Id,
    IReadOnlyList<string>? OwnedBy,
    string? SystemKey,
    string Reference,
    string? Start,
    string Initial,
    JsonArray Context,
    IReadOnlyList<string> ContextNames,
    IReadOnlyDictionary<string, string> States,
    IReadOnlyDictionary<string, GlossaryTrigger> Triggers,
    IReadOnlyDictionary<string, GlossaryGuard> Guards,
    IReadOnlyDictionary<string, GlossaryInvoke> Invokes,
    IReadOnlyDictionary<string, GlossaryLink> Links
)
{
    /// <summary>
    /// A keyed system machine's key parts by name: one instance per key, started and moved by host code and never by
    /// the host at startup. Null for a user's machine and for a system machine at a fixed <see cref="SystemKey"/>.
    /// </summary>
    internal IReadOnlyList<string>? KeyedBy { get; init; }

    /// <summary>Whether the system owns the machine's instances, at a fixed key or keyed.</summary>
    internal bool IsSystem => SystemKey is not null || KeyedBy is not null;

    /// <summary>The machine's definition version, which its snapshots carry and migrations step from. Defaults to 1.</summary>
    internal int Version { get; init; } = 1;

    /// <summary>The context a new instance starts with, when the glossary gives one; else each field's zero.</summary>
    internal JsonObject? InitialContext { get; init; }

    /// <summary>Whether derivation writes the IR's differential block from the scenarios' walks.</summary>
    internal bool Differential { get; init; }

    /// <summary>Each state's shape: its phrase, whether it is committed, its rule and its own context.</summary>
    internal IReadOnlyDictionary<string, GlossaryState> Shapes { get; init; } =
        new Dictionary<string, GlossaryState>(StringComparer.Ordinal);

    /// <summary>The custom rules the machine names, by name.</summary>
    internal IReadOnlyDictionary<string, GlossaryHandler> Rules { get; init; } =
        new Dictionary<string, GlossaryHandler>(StringComparer.Ordinal);

    /// <summary>The custom reductions the machine names, by name.</summary>
    internal IReadOnlyDictionary<string, GlossaryHandler> Reductions { get; init; } =
        new Dictionary<string, GlossaryHandler>(StringComparer.Ordinal);

    /// <summary>The C# type of each forward migration, by the version it steps from.</summary>
    internal IReadOnlyDictionary<int, string> Migrations { get; init; } =
        new Dictionary<int, string>();

    /// <summary>
    /// The custom rules as an in-memory run answers them: each with its example. Derivation and <c>trax machine run</c>
    /// load no assembly, so a scenario that needs the real rule is tagged <c>@host</c>.
    /// </summary>
    internal IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, bool>> ExampleRules =>
        Rules.ToDictionary(
            r => r.Key,
            r =>
            {
                var holds = r.Value.Example.GetValue<bool>();
                return (Func<JsonObject, JsonNode?, bool>)((_, _) => holds);
            },
            StringComparer.Ordinal
        );

    /// <summary>The custom reductions as an in-memory run answers them: the context with the example's fields set.</summary>
    internal IReadOnlyDictionary<
        string,
        Func<JsonObject, JsonNode?, JsonObject>
    > ExampleReductions =>
        Reductions.ToDictionary(
            r => r.Key,
            r =>
            {
                var example = r.Value.Example.AsObject();
                return (Func<JsonObject, JsonNode?, JsonObject>)(
                    (context, _) =>
                    {
                        var next = (JsonObject)context.DeepClone();
                        foreach (var (name, value) in example)
                            next[name] = value?.DeepClone();
                        return next;
                    }
                );
            },
            StringComparer.Ordinal
        );

    /// <summary>A state's context fields: its own, or the machine's.</summary>
    internal JsonArray ContextOf(string state) =>
        Shapes.TryGetValue(state, out var shape) && shape.Context is { } own ? own : Context;

    /// <summary>Every context field name of the machine, its own and its states'.</summary>
    internal IReadOnlySet<string> AllContextNames =>
        ContextNames
            .Concat(Shapes.Values.SelectMany(s => s.ContextNames ?? []))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The type of a context field, from the machine's context and then its states' in name order, or null when no
    /// context has it.
    /// </summary>
    internal string? FieldType(string field) =>
        Context
            .Concat(
                Shapes
                    .Values.OrderBy(s => s.Name, StringComparer.Ordinal)
                    .SelectMany(s => s.Context ?? [])
            )
            .FirstOrDefault(f =>
                string.Equals(f!["name"]!.GetValue<string>(), field, StringComparison.Ordinal)
            )
            ?["type"]?.GetValue<string>();
}
