using System.Text.Json.Nodes;
using Trax.Core.Extensions;

namespace Trax.StateMachine;

// The search the engine kit fixes (conformance/PROTOCOL.md, plan): breadth first from the snapshot, trying the fields
// event and then the navigation triggers in the caller's order from each snapshot, never visiting a wire twice.
internal static class EventPlanner
{
    internal const int MaxExpansions = 10_000;

    // How deep the arrays and objects of a fields input or a context may nest, the outermost counting as one: the Rust
    // engine's limit, which every engine's planner holds a plan to, so they refuse the same values.
    internal const int MaxNesting = 512;

    // A snapshot waiting to be expanded, and, when the goal test already applied the fields event to it, that step and
    // its wire, which the expansion takes instead of advancing it again and then lets go of.
    private sealed record Node(Snapshot Snapshot, Node? Parent, PlannedEvent? Event, int Depth)
    {
        public Applied? Fields { get; set; }
    }

    private sealed record Applied(AdvanceResult Next, string? Key);

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

        var refused = Refuse(graph, snapshot, target, fields, navigation);
        if (refused is not null)
            return refused;

        // A trigger listed twice is tried once, where it is first listed.
        navigation = navigation.Distinct(StringComparer.Ordinal).ToList();
        fields = fields is null ? null : fields with { Input = fields.Input?.DeepClone() };
        var edges = new List<PlannedEvent>(navigation.Count + 1);
        if (fields is not null)
            edges.Add(fields);
        edges.AddRange(navigation.Select(t => new PlannedEvent(t)));
        var depthLimit = maxDepth ?? 2 * graph.StateCount;

        return new Search(graph, target, fields).Run(snapshot, edges, depthLimit);
    }

    // The arguments the search refuses before it starts.
    private static PlanResult.Invalid? Refuse(
        PlanGraph graph,
        Snapshot snapshot,
        string target,
        PlannedEvent? fields,
        IReadOnlyList<string> navigation
    )
    {
        // Checked first, as advance would refuse them: the search copies and compares both.
        if (NestsTooDeep(fields?.Input))
            return new PlanResult.Invalid { Code = RefusalCode.InvalidInput };
        if (NestsTooDeep(snapshot.Context))
            return new PlanResult.Invalid { Code = RefusalCode.InvalidContext };
        if (!graph.IsState(target))
            return new PlanResult.Invalid(Target: target);
        if (fields is not null && !graph.Navigable(fields.Trigger))
            return new PlanResult.Invalid(Trigger: fields.Trigger);
        foreach (var trigger in navigation)
            if (
                !graph.Navigable(trigger)
                || string.Equals(trigger, fields?.Trigger, StringComparison.Ordinal)
            )
                return new PlanResult.Invalid(Trigger: trigger);
        return null;
    }

    // One search: the graph, the target and the fields event it plans for, and what it has met so far.
    private sealed class Search(PlanGraph graph, string target, PlannedEvent? fields)
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly Queue<Node> _queue = new();
        private (string Trigger, Node From)? _undetermined;

        // A host omits a member whose value is null, at any depth, so a snapshot it confirmed never carries one: two
        // snapshots that differ only there are the same.
        private string Key(Snapshot s) =>
            graph.Serialize(s with { Context = WithoutNulls(s.Context) });

        // The goal: the target state, where the fields event, when there is one, changes nothing. A fields event this
        // engine cannot evaluate there, or one the target refuses, never reaches it. `key` is the snapshot's own, and
        // `applied` the fields event's step when the test took one, with its wire when it transitioned.
        private bool IsGoal(Snapshot s, string key, out Applied? applied)
        {
            applied = null;
            if (!string.Equals(s.State, target, StringComparison.Ordinal))
                return false;
            if (fields is null)
                return true;
            if (!graph.Determines(s.State, fields.Trigger))
                return false;
            var next = graph.Advance(s, fields.Trigger, fields.Input?.DeepClone());
            applied = new Applied(
                next,
                next is AdvanceResult.Transitioned moved ? Key(moved.Snapshot) : null
            );
            return string.Equals(applied.Key, key, StringComparison.Ordinal);
        }

        public PlanResult Run(Snapshot snapshot, List<PlannedEvent> edges, int depthLimit)
        {
            var startKey = Key(snapshot);
            if (IsGoal(snapshot, startKey, out var startApplied))
                return new PlanResult.Planned([]);

            _seen.Add(startKey);
            _queue.Enqueue(
                new Node(snapshot, Parent: null, Event: null, 0) { Fields = startApplied }
            );
            var expanded = 0;
            var cut = false;

            while (_queue.Count > 0)
            {
                var node = _queue.Dequeue();
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
                var reached = Expand(node, edges);
                if (reached is not null)
                    return new PlanResult.Planned(PathTo(reached));
                node.Fields = null;
            }

            if (_undetermined is { } u)
                return new PlanResult.Undetermined(u.Trigger, PathTo(u.From));
            return cut ? new PlanResult.Exceeded() : new PlanResult.Unreachable();
        }

        // Tries every edge from one snapshot, queueing the snapshots it reaches first. Returns the node that reached
        // the goal, if one did.
        private Node? Expand(Node node, List<PlannedEvent> edges)
        {
            for (var k = 0; k < edges.Count; k++)
            {
                var edge = edges[k];
                // An edge this engine cannot evaluate is not tried; the first one met is kept in case nothing else
                // reaches the target.
                if (!graph.Determines(node.Snapshot.State, edge.Trigger))
                {
                    _undetermined ??= (edge.Trigger, node);
                    continue;
                }
                // The fields event is the first edge, and the goal test may have taken that step already.
                var reused = k == 0 && fields is not null ? node.Fields : null;
                if (
                    (
                        reused?.Next
                        ?? graph.Advance(node.Snapshot, edge.Trigger, edge.Input?.DeepClone())
                    )
                    is not AdvanceResult.Transitioned next
                )
                    continue;
                var key = reused?.Key ?? Key(next.Snapshot);
                if (!_seen.Add(key))
                    continue;
                var goal = IsGoal(next.Snapshot, key, out var applied);
                var reached = new Node(next.Snapshot, node, edge, node.Depth + 1)
                {
                    Fields = applied,
                };
                if (goal)
                    return reached;
                _queue.Enqueue(reached);
            }
            return null;
        }
    }

    // Whether a value nests deeper than MaxNesting. Never recurses, so a value built in memory cannot exhaust the stack.
    // IrMachine holds an IR and a reduction's context to it too.
    internal static bool NestsTooDeep(JsonNode? value)
    {
        var pending = new Stack<(JsonNode? Node, int Depth)>();
        pending.Push((value, 0));
        while (pending.TryPop(out var next))
        {
            var members = next.Node switch
            {
                JsonObject o => o.Select(m => m.Value),
                JsonArray a => a,
                _ => null,
            };
            if (members is null)
                continue;
            if (next.Depth >= MaxNesting)
                return true;
            foreach (var member in members)
                pending.Push((member, next.Depth + 1));
        }
        return false;
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
        for (var at = node; at.Event is not null; at = at.Parent.NotNull())
            events.Add(at.Event);
        events.Reverse();
        return events;
    }
}
