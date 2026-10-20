using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>What takes a <see cref="MachineEdge"/>: a command someone sends, the state's timer, or an invoked train's outcome.</summary>
internal enum MachineEdgeKind
{
    Command,
    Timer,
    Outcome,
}

/// <summary>
/// One edge of a <see cref="MachineGraph"/>: from a state, on a trigger, to a state, with the guard, reduction and
/// effect the IR names on it. A timer edge also carries how long its state is held.
/// </summary>
/// <param name="From">The state the edge leaves.</param>
/// <param name="Trigger">
/// The trigger that takes it: a command's name, the timer's trigger, or the outcome trigger (<c>Building.done</c>).
/// </param>
/// <param name="To">The state the edge enters.</param>
/// <param name="Kind">What takes the edge.</param>
internal sealed record MachineEdge(string From, string Trigger, string To, MachineEdgeKind Kind)
{
    /// <summary>The guard's message (<c>Because</c>), or the name of its rule; null when the edge is unguarded.</summary>
    public string? Guard { get; init; }

    /// <summary>The name of the edge's reduction, or null when it keeps the context.</summary>
    public string? Reduce { get; init; }

    /// <summary>The key prefix of the effect the edge runs exactly once, or null when it runs none.</summary>
    public string? Effect { get; init; }

    /// <summary>
    /// How long a timer edge's state is held: an ISO-8601 duration (<c>PT10M</c>), or a context field with its
    /// bounds. Null for any other edge.
    /// </summary>
    public string? Timer { get; init; }
}

/// <summary>A timed state's timer as the IR declares it: a fixed duration, or one a context field holds within bounds.</summary>
/// <param name="State">The timed state.</param>
/// <param name="Trigger">The timer's trigger.</param>
/// <param name="Target">The state the timer moves the instance to.</param>
internal sealed record MachineTimer(string State, string Trigger, string Target)
{
    /// <summary>The fixed duration (ISO-8601), or null when a context field holds it.</summary>
    public string? After { get; init; }

    /// <summary>The context field that holds the duration, or null for a fixed one.</summary>
    public string? Field { get; init; }

    /// <summary>The shortest duration the field may hold (ISO-8601); null for a fixed one.</summary>
    public string? Min { get; init; }

    /// <summary>The longest duration the field may hold (ISO-8601); null for a fixed one.</summary>
    public string? Max { get; init; }

    /// <summary>The duration as one line: the fixed duration, or the field with its bounds.</summary>
    public string Duration => After ?? (Field is not null ? $"{Field} ({Min} to {Max})" : "?");
}

/// <summary>
/// A machine's IR (<see cref="IMachine.ExportIr"/>) read into states and edges: the one reading of the IR that
/// <c>trax machine show</c> draws and the operations surface serves. Commands come first in IR order, then the
/// timers, then each invoking state's outcomes in the order they are tried. Pure: it reads text, so it is tested
/// without an assembly.
/// </summary>
/// <param name="Id">The machine's id.</param>
/// <param name="Version">Its definition version, as the IR writes it.</param>
/// <param name="Initial">The state an instance starts in.</param>
/// <param name="States">Every state, in IR order.</param>
/// <param name="Committed">The states an instance cannot leave once the server has put it there.</param>
/// <param name="Transitions">Every edge.</param>
/// <param name="Timers">Every timed state's timer, in IR order.</param>
internal sealed record MachineGraph(
    string Id,
    string Version,
    string Initial,
    IReadOnlyList<string> States,
    IReadOnlySet<string> Committed,
    IReadOnlyList<MachineEdge> Transitions,
    IReadOnlyList<MachineTimer> Timers
)
{
    /// <summary>Reads <paramref name="irJson"/>.</summary>
    /// <exception cref="InvalidOperationException">The IR is not an object, or lacks a field every IR has.</exception>
    public static MachineGraph Parse(string irJson)
    {
        var root =
            JsonNode.Parse(irJson) as JsonObject
            ?? throw new InvalidOperationException("The machine's IR is not a JSON object.");

        static string Required(JsonObject o, string name) =>
            o[name]?.ToString()
            ?? throw new InvalidOperationException($"The machine's IR has no '{name}'.");

        var transitions = (root["transitions"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(t => new MachineEdge(
                Required(t, "from"),
                Required(t, "trigger"),
                Required(t, "to"),
                MachineEdgeKind.Command
            )
            {
                Guard =
                    t["guardMessage"]?.ToString()
                    ?? (t["guard"] as JsonObject)?["rule"]?.ToString(),
                Reduce = (t["reduce"] as JsonObject)?["reduce"]?.ToString(),
                Effect = (t["effect"] as JsonObject)?["keyPrefix"]?.ToString(),
            })
            .ToList();

        // A timer is an edge the server takes once its state has been held long enough: fixed (`after`), or for the
        // duration a context field holds within its bounds.
        var timers = (root["timers"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(t => new MachineTimer(
                Required(t, "state"),
                Required(t, "trigger"),
                Required(t, "target")
            )
            {
                After = t["after"]?.ToString(),
                Field = (t["field"] as JsonObject)?["name"]?.ToString(),
                Min = (t["field"] as JsonObject)?["min"]?.ToString(),
                Max = (t["field"] as JsonObject)?["max"]?.ToString(),
            })
            .ToList();
        transitions.AddRange(
            timers.Select(t => new MachineEdge(t.State, t.Trigger, t.Target, MachineEdgeKind.Timer)
            {
                Timer = t.Duration,
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
                    .Select(e => new MachineEdge(
                        from,
                        trigger,
                        Required(e, "to"),
                        MachineEdgeKind.Outcome
                    )
                    {
                        Guard = (e["guard"] as JsonObject)?["rule"]?.ToString(),
                        Reduce = (e["reduce"] as JsonObject)?["reduce"]?.ToString(),
                    })
            );
        }

        return new MachineGraph(
            Required(root, "id"),
            root["version"]?.ToString() ?? "?",
            Required(root, "initialState"),
            (root["states"] as JsonArray ?? []).Select(s => s!.ToString()).ToList(),
            (root["committedStates"] as JsonArray ?? [])
                .Select(s => s!.ToString())
                .ToHashSet(StringComparer.Ordinal),
            transitions,
            timers
        );
    }

    /// <summary>Every state some path of edges reaches from <see cref="Initial"/>, the initial state included.</summary>
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
