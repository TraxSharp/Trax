using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>One event of a plan: a trigger, and its input when it takes one.</summary>
/// <param name="Trigger">The trigger's name (<c>Next</c>).</param>
/// <param name="Input">The trigger's input, or null when it takes none.</param>
public sealed record PlannedEvent(string Trigger, JsonNode? Input = null);

/// <summary>
/// The total result of a plan (<see cref="IrMachine.Plan"/>, <see cref="BuiltMachine{TState,TTrigger}.Plan"/>): the
/// events that reach the target, or why there are none. Never an exception.
/// </summary>
public abstract record PlanResult
{
    /// <summary>The events that take the snapshot to the target, in order; empty when it is there already.</summary>
    public sealed record Planned(IReadOnlyList<PlannedEvent> Events) : PlanResult;

    /// <summary>The search ran out of snapshots without reaching the target.</summary>
    public sealed record Unreachable : PlanResult;

    /// <summary>The search stopped at its depth or its size before it could tell.</summary>
    public sealed record Exceeded : PlanResult;

    /// <summary>
    /// No plan, but <see cref="Trigger"/> leaves from the snapshot <see cref="Events"/> reach on an edge that rests on a
    /// custom rule this engine holds no handler for. Send the events, then the trigger with no prediction, and plan
    /// again from the server's answer.
    /// </summary>
    public sealed record Undetermined(string Trigger, IReadOnlyList<PlannedEvent> Events)
        : PlanResult;

    /// <summary>
    /// The request cannot be planned: <see cref="Target"/> is not a state, or <see cref="Trigger"/> is not one a client
    /// may send to move (one the machine does not have, one bound to the effect, an outcome, a timer, or a navigation
    /// trigger that is also the fields event). Exactly one of the two is set.
    /// </summary>
    public sealed record Invalid(string? Trigger = null, string? Target = null) : PlanResult;

    private PlanResult() { }
}

// What the planner needs of an engine: its states, which triggers a client may move with, which edges it can evaluate,
// and its advance and wire. The search itself is the same for every engine, so it answers the same plan.
internal sealed record PlanGraph(
    int StateCount,
    Func<string, bool> IsState,
    Func<string, bool> Navigable,
    Func<string, string, bool> Determines,
    Func<Snapshot, string, JsonNode?, AdvanceResult> Advance,
    Func<Snapshot, string> Serialize
);

// The search the engine kit fixes (conformance/PROTOCOL.md, plan): breadth first from the snapshot, trying the fields
// event and then the navigation triggers in the caller's order from each snapshot, never visiting a wire twice.
internal static class EventPlanner
{
    internal const int MaxExpansions = 10_000;

    private sealed record Node(Snapshot Snapshot, Node? Parent, PlannedEvent? Event, int Depth);

    public static PlanResult Plan(
        PlanGraph graph,
        Snapshot snapshot,
        string target,
        PlannedEvent? fields,
        IReadOnlyList<string> navigation,
        int? maxDepth
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(navigation);

        if (!graph.IsState(target))
            return new PlanResult.Invalid(Target: target);
        if (fields is not null && !graph.Navigable(fields.Trigger))
            return new PlanResult.Invalid(Trigger: fields.Trigger);
        foreach (var trigger in navigation)
            if (!graph.Navigable(trigger) || trigger == fields?.Trigger)
                return new PlanResult.Invalid(Trigger: trigger);

        fields = fields is null ? null : fields with { Input = fields.Input?.DeepClone() };
        var edges = new List<PlannedEvent>(navigation.Count + 1);
        if (fields is not null)
            edges.Add(fields);
        edges.AddRange(navigation.Select(t => new PlannedEvent(t)));
        var depthLimit = maxDepth ?? 2 * graph.StateCount;

        // A host omits a member whose value is null, at any depth, so a snapshot it confirmed never carries one: two
        // snapshots that differ only there are the same.
        string Key(Snapshot s) => graph.Serialize(s with { Context = WithoutNulls(s.Context) });

        bool IsGoal(Snapshot s)
        {
            if (s.State != target)
                return false;
            if (fields is null)
                return true;
            // A fields event this engine cannot evaluate there, or one the target refuses, never reaches the goal.
            if (!graph.Determines(s.State, fields.Trigger))
                return false;
            return graph.Advance(s, fields.Trigger, fields.Input?.DeepClone())
                    is AdvanceResult.Transitioned applied
                && Key(applied.Snapshot) == Key(s);
        }

        if (IsGoal(snapshot))
            return new PlanResult.Planned([]);

        var seen = new HashSet<string>(StringComparer.Ordinal) { Key(snapshot) };
        var queue = new Queue<Node>();
        queue.Enqueue(new Node(snapshot, null, null, 0));
        var expanded = 0;
        var cut = false;
        (string Trigger, Node From)? undetermined = null;

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.Depth >= depthLimit)
            {
                cut = true;
                continue;
            }
            // A snapshot still waiting after the last expansion the search may make.
            if (expanded == MaxExpansions)
            {
                cut = true;
                break;
            }
            expanded++;
            foreach (var edge in edges)
            {
                // An edge this engine cannot evaluate is not tried; the first one met is kept in case nothing else
                // reaches the target.
                if (!graph.Determines(node.Snapshot.State, edge.Trigger))
                {
                    undetermined ??= (edge.Trigger, node);
                    continue;
                }
                if (
                    graph.Advance(node.Snapshot, edge.Trigger, edge.Input?.DeepClone())
                    is not AdvanceResult.Transitioned next
                )
                    continue;
                if (!seen.Add(Key(next.Snapshot)))
                    continue;
                var reached = new Node(next.Snapshot, node, edge, node.Depth + 1);
                if (IsGoal(reached.Snapshot))
                    return new PlanResult.Planned(PathTo(reached));
                queue.Enqueue(reached);
            }
        }

        if (undetermined is { } u)
            return new PlanResult.Undetermined(u.Trigger, PathTo(u.From));
        return cut ? new PlanResult.Exceeded() : new PlanResult.Unreachable();
    }

    // The object with every member whose value is null left out, at every depth, as the client reads a host's answer.
    // An array keeps its null elements.
    private static JsonObject WithoutNulls(JsonObject node)
    {
        var result = new JsonObject();
        foreach (var (name, value) in node)
            if (value is not null)
                result[name] = Strip(value);
        return result;
    }

    private static JsonNode Strip(JsonNode node) =>
        node switch
        {
            JsonObject o => WithoutNulls(o),
            JsonArray a => new JsonArray(a.Select(v => v is null ? null : Strip(v)).ToArray()),
            _ => node.DeepClone(),
        };

    private static List<PlannedEvent> PathTo(Node node)
    {
        var events = new List<PlannedEvent>();
        for (var at = node; at.Event is not null; at = at.Parent!)
            events.Add(at.Event);
        events.Reverse();
        return events;
    }
}
