using System.Text;
using System.Text.Json.Nodes;

namespace Trax.Cli.Machines;

/// <summary>How <c>trax machine show</c> draws a machine.</summary>
internal enum MachineFormat
{
    Text,
    Mermaid,
    Dot,
}

/// <summary>
/// Renders a machine's IR (<c>Machine.ExportIr()</c>) for <c>trax machine show</c>: a plain-text block per state
/// for a terminal, or a Mermaid <c>stateDiagram-v2</c> or Graphviz source for docs and PRs. Pure, so it is tested
/// from IR text without an assembly. The text form lists each state's outgoing edges rather than laying out a
/// 2-D graph, which stays readable and diffs cleanly however many states there are. A state's timer is drawn as an
/// edge of its own, under its trigger (<c>Review.after</c>) and marked with how long the state is held, since only
/// the server fires it. An invoked train's outcome is drawn the same way, under its trigger (<c>Dealing.done</c>).
/// </summary>
internal static class MachineRenderer
{
    public static string Render(
        string irJson,
        MachineFormat format,
        bool ascii = false,
        bool color = false
    )
    {
        var ir = Ir.Parse(irJson);
        return format switch
        {
            // The diagram sources are printed too; their only control character of their own is the newline.
            MachineFormat.Mermaid => Safe(Mermaid(ir), keepNewlines: true),
            MachineFormat.Dot => Safe(Dot(ir), keepNewlines: true),
            _ => Text(ir, ascii ? Glyphs.Ascii : Glyphs.Unicode, color),
        };
    }

    private static string Text(Ir ir, Glyphs g, bool color)
    {
        string Paint(string text, string code) => color ? $"\u001b[{code}m{text}\u001b[0m" : text;

        // Every name and message comes from the machine's assembly and goes to a terminal, so control characters
        // (an escape sequence that recolours, clears or retitles the terminal, a carriage return that overwrites
        // the line) are shown as '?' rather than sent.
        var sb = new StringBuilder();
        sb.Append($"{Safe(ir.Id)}  v{Safe(ir.Version)}   initial: {Safe(ir.Initial)}");
        if (ir.Committed.Count > 0)
            sb.Append($"   committed: {string.Join(", ", ir.Committed.Select(c => Safe(c)))}");
        sb.Append('\n');

        var reachable = ir.Reachable();
        var triggerWidth = ir
            .Transitions.Select(t => Safe(t.Trigger).Length)
            .DefaultIfEmpty(0)
            .Max();
        var targetWidth = ir.Transitions.Select(t => Safe(t.To).Length).DefaultIfEmpty(0).Max();

        foreach (var state in ir.States)
        {
            sb.Append('\n');
            var marks = new List<string>();
            if (state == ir.Initial)
                marks.Add($"{g.Initial} initial");
            if (ir.Committed.Contains(state))
                marks.Add($"{g.Committed} committed");
            if (!reachable.Contains(state))
                marks.Add("unreachable from the initial state");
            sb.Append(Paint(Safe(state), "1"));
            if (marks.Count > 0)
                sb.Append(' ').Append(string.Join("  ", marks));
            sb.Append('\n');

            var edges = ir.Transitions.Where(t => t.From == state).ToList();
            if (edges.Count == 0)
            {
                sb.Append("  (no transitions)\n");
                continue;
            }

            foreach (var edge in edges)
            {
                var trigger = Safe(edge.Trigger);
                var line = new StringBuilder("  ")
                    .Append(g.Dash)
                    .Append(' ')
                    .Append(trigger)
                    .Append(' ')
                    .Append(new string(g.DashChar, triggerWidth - trigger.Length + 2))
                    .Append(g.Arrow)
                    .Append(' ')
                    .Append(Safe(edge.To).PadRight(targetWidth));

                var notes = new List<string>();
                if (edge.Guard is not null)
                    notes.Add($"guard: {Safe(edge.Guard)}");
                if (edge.Reduce is not null)
                    notes.Add($"reduce: {Safe(edge.Reduce)}");
                if (edge.Effect is not null)
                    notes.Add(Paint($"{g.Effect} effect {Safe(edge.Effect)} (send only)", "33"));
                if (edge.Timer is not null)
                    notes.Add(Paint($"{g.Timer} after {Safe(edge.Timer)} (server only)", "36"));
                if (notes.Count > 0)
                    line.Append("   ").Append(string.Join("   ", notes));
                sb.Append(line.ToString().TrimEnd()).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// <paramref name="text"/> with every control character (C0, DEL and C1) replaced by '?', except '\n' when
    /// <paramref name="keepNewlines"/> is set.
    /// </summary>
    internal static string Safe(string text, bool keepNewlines = false)
    {
        bool Unsafe(char c) => char.IsControl(c) && !(keepNewlines && c == '\n');

        if (!text.Any(Unsafe))
            return text;
        return string.Concat(text.Select(c => Unsafe(c) ? '?' : c));
    }

    private static string Mermaid(Ir ir)
    {
        var sb = new StringBuilder("stateDiagram-v2\n");
        sb.Append($"    [*] --> {ir.Initial}\n");
        foreach (
            var state in ir.States.Where(s => !ir.Transitions.Any(t => t.From == s || t.To == s))
        )
            sb.Append($"    {state}\n");
        foreach (var edge in ir.Transitions)
            sb.Append($"    {edge.From} --> {edge.To} : {MermaidText(Label(edge, " / "))}\n");
        if (ir.Committed.Count > 0)
        {
            sb.Append("    classDef committed font-weight:bold,stroke-width:3px\n");
            sb.Append($"    class {string.Join(",", ir.Committed)} committed\n");
        }
        return sb.ToString();
    }

    private static string Dot(Ir ir)
    {
        var sb = new StringBuilder($"digraph \"{DotText(ir.Id)}\" {{\n");
        sb.Append("    rankdir=LR;\n");
        sb.Append("    node [shape=box, style=rounded];\n");
        sb.Append("    __initial [shape=point, label=\"\"];\n");
        sb.Append($"    __initial -> \"{DotText(ir.Initial)}\";\n");
        foreach (var state in ir.States)
            sb.Append(
                ir.Committed.Contains(state)
                    ? $"    \"{DotText(state)}\" [peripheries=2];\n"
                    : $"    \"{DotText(state)}\";\n"
            );
        foreach (var edge in ir.Transitions)
            sb.Append(
                $"    \"{DotText(edge.From)}\" -> \"{DotText(edge.To)}\" [label=\"{DotText(Label(edge, "\n"))}\"];\n"
            );
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string Label(Transition edge, string separator)
    {
        var parts = new List<string> { edge.Trigger };
        if (edge.Guard is not null)
            parts.Add($"guard: {edge.Guard}");
        if (edge.Reduce is not null)
            parts.Add($"reduce: {edge.Reduce}");
        if (edge.Effect is not null)
            parts.Add($"effect {edge.Effect} (send only)");
        if (edge.Timer is not null)
            parts.Add($"after {edge.Timer} (server only)");
        return string.Join(separator, parts);
    }

    // Mermaid ends a label at ';' and reads '#' as an entity and ':' as a separator, so those are entity-coded.
    private static string MermaidText(string text) =>
        text.Replace("#", "#35;")
            .Replace(";", "#59;")
            .Replace(":", "#58;")
            .Replace("\"", "#quot;")
            .Replace("\n", " ");

    private static string DotText(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    private sealed record Glyphs(
        string Initial,
        string Committed,
        string Dash,
        char DashChar,
        string Arrow,
        string Effect,
        string Timer
    )
    {
        public static readonly Glyphs Unicode = new("◀", "■", "──", '─', "▶", "⚡", "⏱");
        public static readonly Glyphs Ascii = new("<-", "[x]", "--", '-', ">", "!", "@");
    }

    private sealed record Transition(
        string From,
        string Trigger,
        string To,
        string? Guard,
        string? Reduce,
        string? Effect
    )
    {
        /// <summary>How long a timer edge's state is held (<c>PT10M</c>, or a context field with its bounds); null for an event.</summary>
        public string? Timer { get; init; }
    }

    private sealed record Ir(
        string Id,
        string Version,
        string Initial,
        IReadOnlyList<string> States,
        IReadOnlySet<string> Committed,
        IReadOnlyList<Transition> Transitions
    )
    {
        public static Ir Parse(string json)
        {
            var root =
                JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("The machine's IR is not a JSON object.");

            string Required(JsonObject o, string name) =>
                o[name]?.ToString()
                ?? throw new InvalidOperationException($"The machine's IR has no '{name}'.");

            var transitions = (root["transitions"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(t => new Transition(
                    Required(t, "from"),
                    Required(t, "trigger"),
                    Required(t, "to"),
                    t["guardMessage"]?.ToString()
                        ?? (t["guard"] as JsonObject)?["rule"]?.ToString(),
                    (t["reduce"] as JsonObject)?["reduce"]?.ToString(),
                    (t["effect"] as JsonObject)?["keyPrefix"]?.ToString()
                ))
                .ToList();

            // A timer is an edge the server takes once its state has been held long enough: fixed (`after`), or for
            // the duration a context field holds within its bounds.
            transitions.AddRange(
                (root["timers"] as JsonArray ?? [])
                    .OfType<JsonObject>()
                    .Select(t => new Transition(
                        Required(t, "state"),
                        Required(t, "trigger"),
                        Required(t, "target"),
                        null,
                        null,
                        null
                    )
                    {
                        Timer =
                            t["after"]?.ToString()
                            ?? (
                                t["field"] is JsonObject field
                                    ? $"{field["name"]} ({field["min"]} to {field["max"]})"
                                    : "?"
                            ),
                    })
            );

            // A state that invokes a train is left by the train's outcome (Dealing.done, .failed, .cancelled), whose
            // edges are tried in order. They are edges like any other: a state reached only through one is reachable.
            foreach (var (trigger, node) in root["outcomes"] as JsonObject ?? [])
            {
                if (node is not JsonObject outcome)
                    continue;
                var from = Required(outcome, "state");
                transitions.AddRange(
                    (outcome["edges"] as JsonArray ?? [])
                        .OfType<JsonObject>()
                        .Select(e => new Transition(
                            from,
                            trigger,
                            Required(e, "to"),
                            (e["guard"] as JsonObject)?["rule"]?.ToString(),
                            (e["reduce"] as JsonObject)?["reduce"]?.ToString(),
                            null
                        ))
                );
            }

            return new Ir(
                Required(root, "id"),
                root["version"]?.ToString() ?? "?",
                Required(root, "initialState"),
                (root["states"] as JsonArray ?? []).Select(s => s!.ToString()).ToList(),
                (root["committedStates"] as JsonArray ?? [])
                    .Select(s => s!.ToString())
                    .ToHashSet(StringComparer.Ordinal),
                transitions
            );
        }

        public HashSet<string> Reachable()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { Initial };
            var queue = new Queue<string>([Initial]);
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                foreach (var edge in Transitions.Where(t => t.From == state))
                    if (seen.Add(edge.To))
                        queue.Enqueue(edge.To);
            }
            return seen;
        }
    }
}
