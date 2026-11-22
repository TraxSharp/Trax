using System.Text.Json.Nodes;
using Trax.Core.Extensions;

namespace Trax.StateMachine.Persistence;

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
[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
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

        var timers = ReadTimers(root);
        transitions.AddRange(
            timers.Select(t => new MachineEdge(t.State, t.Trigger, t.Target, MachineEdgeKind.Timer)
            {
                Timer = t.Duration,
            })
        );

        AddOutcomeEdges(root, transitions);

        return new MachineGraph(
            Required(root, "id"),
            root["version"]?.ToString() ?? "?",
            Required(root, "initialState"),
            (root["states"] as JsonArray ?? []).Select(s => s.NotNull().ToString()).ToList(),
            (root["committedStates"] as JsonArray ?? [])
                .Select(s => s.NotNull().ToString())
                .ToHashSet(StringComparer.Ordinal),
            transitions,
            timers
        );
    }

    private static string Required(JsonObject o, string name) =>
        o[name]?.ToString()
        ?? throw new InvalidOperationException($"The machine's IR has no '{name}'.");

    private static List<MachineTimer> ReadTimers(JsonObject root)
    {
        // A timer is an edge the server takes once its state has been held long enough: fixed (`after`), or for the
        // duration a context field holds within its bounds.
        return (root["timers"] as JsonArray ?? [])
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
    }

    private static void AddOutcomeEdges(JsonObject root, List<MachineEdge> transitions)
    {
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
    }

    /// <summary>Every state some path of edges reaches from <see cref="Initial"/>, the initial state included.</summary>
    public HashSet<string> Reachable()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { Initial };
        var queue = new Queue<string>([Initial]);
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            foreach (
                var edge in Transitions.Where(t =>
                    string.Equals(t.From, state, StringComparison.Ordinal)
                )
            )
                if (seen.Add(edge.To))
                    queue.Enqueue(edge.To);
        }
        return seen;
    }
}
