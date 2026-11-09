using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// The policies that would set each other off with nothing to pace them (ADR <c>Trax.Docs/adr/0058</c>). Entering a
/// state sets off the policies waiting for it. A policy fires a trigger, which can move its machine into any state an
/// edge of that trigger goes to, and a state that runs a train can move on into any state its outcomes go to. A loop of
/// those moves alone has the server write transitions for as long as it runs, so derivation and the host both refuse
/// one, through this one check, and give the same answer. A loop that passes through a timer is paced by it and is
/// allowed, as is one a client's trigger closes. A self-transition sets nothing off, so it is no move here. The same
/// moves find a policy that waits for another's result (<see cref="Races"/>), which derivation warns of.
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
        var moves = Moves(policies, irs);
        var reach = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> Reach(string from) =>
            reach.TryGetValue(from, out var known) ? known : reach[from] = Reached(from, moves);
        var fired = moves.Where(m => m.Policy is not null).ToList();

        // A policy is in a loop when the state it moves an instance into comes back to the one that set it off. Two
        // such policies are in the same loop when their sources reach each other.
        var loops = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (from, into, id) in fired)
        {
            if (into != from && !Reach(into).Contains(from))
                continue;
            var key = Reach(from)
                .Where(s => Reach(s).Contains(from))
                .Append(from)
                .Min(StringComparer.Ordinal)!;
            if (!loops.TryGetValue(key, out var loop))
                loops[key] = loop = new SortedSet<string>(StringComparer.Ordinal);
            loop.Add(id!);
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

    /// <summary>
    /// A policy that moves instances waiting in <see cref="State"/>, which <see cref="Earlier"/> moves them into, when
    /// <see cref="Later"/>'s state can be reached from <see cref="Earlier"/>'s with no person's move in between. In
    /// memory a When plays its whole cascade in the order its Thens list it; on the host each policy fire and each
    /// invoked train runs on its own, in no fixed order, so <see cref="Later"/> may find nothing waiting yet.
    /// </summary>
    internal sealed record Race(HostPolicy Later, HostPolicy Earlier, string State);

    /// <summary>
    /// The policy runner's default interval, the time it waits between passes when nothing is owed:
    /// <c>PolicyRunnerOptions.Interval</c> defaults to this. A timer shorter than it can fire before the runner's next
    /// pass, so <see cref="Races"/> does not count one as spacing.
    /// </summary>
    internal static readonly TimeSpan RunnerInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Each policy that waits for another's result (<see cref="Race"/>), sorted by the later policy and then the earlier.
    /// The moves between them are the ones <see cref="Find"/> follows, but not the earlier policy's own: a state reached
    /// only through its landing comes after it. The earlier policy's result is where its landing moves an instance, and
    /// any state a train's outcome or a short timer moves it on to from there. A timer of <see cref="RunnerInterval"/> or more, or one whose duration a
    /// context field holds, is no such move: it waits a time a person chose, which the policy runner's interval does
    /// not come near, as a loop through one is paced by it. A fixed timer shorter than the interval can fire before the
    /// runner's next pass, so it is one.
    /// </summary>
    internal static List<Race> Races(
        IReadOnlyList<HostPolicy> policies,
        IReadOnlyDictionary<string, JsonObject> irs
    )
    {
        var moves = Moves(policies, irs);
        moves.AddRange(ShortTimers(irs));
        var unpaced = moves.Where(m => m.Policy is null).ToList();
        var races = new List<Race>();
        foreach (var later in policies)
        {
            if (!irs.TryGetValue(later.Target, out var target))
                continue;
            // A broadcast reaches the instances waiting in one state; any other policy those its trigger leaves.
            var waits =
                later.Shape == "broadcast"
                    ? new List<string> { later.By }
                    : Array(target["transitions"])
                        .Where(e => Text(e?["trigger"]) == later.Trigger)
                        .Select(e => Text(e?["from"]))
                        .OfType<string>()
                        .Distinct()
                        .ToList();
            foreach (var earlier in policies)
            {
                if (earlier.Id == later.Id || earlier.Target != later.Target)
                    continue;
                // The states the earlier policy's landing moves an instance into, and those a train or a short timer
                // moves it on to from there: those land later still.
                var landings = moves.Where(m => m.Policy == earlier.Id).Select(m => m.To).ToList();
                var into = landings
                    .Concat(landings.SelectMany(l => Reached(l, unpaced)))
                    .ToHashSet(StringComparer.Ordinal);
                if (
                    waits.FirstOrDefault(w => into.Contains($"{later.Target}.{w}")) is not { } state
                )
                    continue;
                var from = $"{earlier.When}.{earlier.Enters}";
                var to = $"{later.When}.{later.Enters}";
                if (
                    from == to
                    || Reached(from, moves.Where(m => m.Policy != earlier.Id)).Contains(to)
                )
                    races.Add(new Race(later, earlier, state));
            }
        }
        return races
            .OrderBy(r => r.Later.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Earlier.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>What warns of a <see cref="Race"/>, naming both policies.</summary>
    internal static string Warning(Race race) =>
        $"When {race.Later.When} enters {race.Later.Enters}, the policy {race.Later.Id} moves {race.Later.Target} instances in {race.State}; "
        + $"they reach {race.State} through {race.Earlier.Id}, a policy set off when {race.Earlier.When} enters {race.Earlier.Enters}, "
        + "and on the host that policy may land after this one: each policy and each train runs on its own, in no fixed order. "
        + "Don't let one policy wait for another's result: move them from a state the earlier policy leaves alone, or move them in one policy.";

    // A move from one state to another that nothing paces, as machine.State: a policy firing (named by its id), and a
    // train's outcome moving its instance on from the state that ran it.
    private sealed record Move(string From, string To, string? Policy);

    private static List<Move> Moves(
        IEnumerable<HostPolicy> policies,
        IReadOnlyDictionary<string, JsonObject> irs
    )
    {
        var moves = new List<Move>();
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
                    moves.Add(new Move(from, $"{policy.Target}.{to}", policy.Id));
        }

        // A train's outcome moves its instance on from the state that ran it, with no client and no timer in between.
        foreach (var (id, ir) in irs)
        foreach (var (_, outcome) in ir["outcomes"] as JsonObject ?? [])
        {
            if (Text(outcome?["state"]) is not { } state)
                continue;
            foreach (var edge in Array(outcome?["edges"]))
                if (Text(edge?["to"]) is { } to && to != state)
                    moves.Add(new Move($"{id}.{state}", $"{id}.{to}", null));
        }
        return moves;
    }

    // A timer with a fixed duration shorter than the runner's interval, moving its instance on from the state that
    // holds it. A loop through one is still paced, so only Races follows these.
    private static IEnumerable<Move> ShortTimers(IReadOnlyDictionary<string, JsonObject> irs)
    {
        foreach (var (id, ir) in irs)
        foreach (var timer in Array(ir["timers"]))
            if (
                Text(timer?["state"]) is { } state
                && Text(timer?["target"]) is { } target
                && target != state
                && IsoDuration.TryParse(Text(timer?["after"]), out var after)
                && after < RunnerInterval
            )
                yield return new Move($"{id}.{state}", $"{id}.{target}", null);
    }

    // Every state the moves reach from one, not counting it unless a move comes back to it.
    private static HashSet<string> Reached(string from, IEnumerable<Move> moves)
    {
        var next = moves
            .GroupBy(m => m.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.To).ToList(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([from]);
        while (queue.TryDequeue(out var at))
            foreach (var to in next.GetValueOrDefault(at) ?? [])
                if (seen.Add(to))
                    queue.Enqueue(to);
        return seen;
    }

    private static JsonArray Array(JsonNode? node) => node as JsonArray ?? [];

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
