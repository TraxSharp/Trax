using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// The policies that would set each other off with nothing to pace them (ADR <c>Trax.Docs/adr/0058</c>). Entering a
/// state sets off the policies waiting for it. A policy fires a trigger, which can move its machine into any state an
/// edge of that trigger goes to, and a state that runs a train can move on into any state its outcomes go to. A loop of
/// those moves alone has the server write transitions for as long as it runs, so derivation and the host both refuse
/// one, through this one check, and give the same answer. A loop that passes through a timer is paced by it and is
/// allowed, as is one a client's trigger closes. A self-transition sets nothing off, so it is no move here.
/// </summary>
internal static class PolicyLoops
{
    /// <summary>
    /// Each loop, as the ids of the policies in it (<see cref="HostPolicy.Id"/>), sorted. Empty when there is none. A
    /// policy or machine the IRs do not have adds no move; the pairing check names it.
    /// </summary>
    internal static List<IReadOnlyList<string>> Find(
        IEnumerable<HostPolicy> policies,
        IReadOnlyDictionary<string, JsonObject> irs
    )
    {
        var moves = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Move(string from, string to)
        {
            if (!moves.TryGetValue(from, out var next))
                moves[from] = next = new HashSet<string>(StringComparer.Ordinal);
            next.Add(to);
        }

        var fired = new List<(string Id, string From, string To)>();
        foreach (var policy in policies)
        {
            if (!irs.TryGetValue(policy.Target, out var target))
                continue;
            var from = $"{policy.When}.{policy.Enters}";
            foreach (var edge in Array(target["transitions"]))
                if (
                    Text(edge?["trigger"]) == policy.Trigger
                    && Text(edge?["from"]) is { } edgeFrom
                    && Text(edge?["to"]) is { } to
                    && edgeFrom != to
                )
                {
                    var into = $"{policy.Target}.{to}";
                    fired.Add((policy.Id, from, into));
                    Move(from, into);
                }
        }

        // A train's outcome moves its instance on from the state that ran it, with no client and no timer in between.
        foreach (var (id, ir) in irs)
        foreach (var (_, outcome) in ir["outcomes"] as JsonObject ?? [])
        {
            if (Text(outcome?["state"]) is not { } state)
                continue;
            foreach (var edge in Array(outcome?["edges"]))
                if (Text(edge?["to"]) is { } to && to != state)
                    Move($"{id}.{state}", $"{id}.{to}");
        }

        var reach = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> Reach(string from)
        {
            if (reach.TryGetValue(from, out var known))
                return known;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>([from]);
            while (queue.TryDequeue(out var at))
                foreach (var next in moves.GetValueOrDefault(at) ?? [])
                    if (seen.Add(next))
                        queue.Enqueue(next);
            return reach[from] = seen;
        }

        // A policy is in a loop when the state it moves an instance into comes back to the one that set it off. Two
        // such policies are in the same loop when their sources reach each other.
        var loops = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (id, from, into) in fired)
        {
            if (into != from && !Reach(into).Contains(from))
                continue;
            var key = Reach(from)
                .Where(s => Reach(s).Contains(from))
                .Append(from)
                .Min(StringComparer.Ordinal)!;
            if (!loops.TryGetValue(key, out var loop))
                loops[key] = loop = new SortedSet<string>(StringComparer.Ordinal);
            loop.Add(id);
        }
        return loops
            .Values.Select(l => (IReadOnlyList<string>)l.ToList())
            .OrderBy(l => l[0], StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>What refuses a loop <see cref="Find"/> found, naming its policies.</summary>
    internal static string Problem(IReadOnlyList<string> loop) =>
        (
            loop.Count == 1
                ? $"The policy {loop[0]} sets itself off"
                : $"The policies {string.Join(", ", loop.Take(loop.Count - 1))} and {loop[^1]} set each other off"
        )
        + " with no timer between them, so the server would move instances in a loop that never ends. Break the loop, or let a timer close it.";

    private static JsonArray Array(JsonNode? node) => node as JsonArray ?? [];

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
