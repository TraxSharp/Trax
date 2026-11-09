namespace Trax.StateMachine;

/// <summary>
/// The loops a machine's timers make on their own: each state has at most one timer, so following timers from any
/// state either ends or comes round to a state it passed, a loop no event breaks and the instance goes round forever.
/// The builder refuses a machine with one, and derivation reads the same loops, so it never writes a machine the host
/// then refuses.
/// </summary>
internal static class TimerLoops
{
    /// <summary>
    /// Each loop once, as its states in order from the first of them in <paramref name="order"/>, ending where it began.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<TState>> Find<TState>(
        IReadOnlyDictionary<TState, TState> next,
        IReadOnlyList<TState> order
    )
        where TState : notnull
    {
        var loops = new List<IReadOnlyList<TState>>();
        var named = new HashSet<TState>();
        foreach (var start in order)
        {
            var path = new List<TState>();
            var current = start;
            while (next.TryGetValue(current, out var target) && !path.Contains(current))
            {
                path.Add(current);
                current = target;
            }
            if (!path.Contains(current) || named.Contains(current))
                continue;
            var loop = path.SkipWhile(s => !EqualityComparer<TState>.Default.Equals(s, current))
                .ToList();
            var first = loop.MinBy(s => IndexOf(order, s));
            var at = loop.IndexOf(first!);
            loops.Add(loop.Skip(at).Concat(loop.Take(at)).Append(first!).ToList());
            foreach (var s in loop)
                named.Add(s);
        }
        return loops;
    }

    /// <summary>The problem a loop is, worded once for the builder and derivation alike.</summary>
    internal static string Describe<TState>(IEnumerable<TState> loop) =>
        $"the timers of {string.Join(" -> ", loop)} form a cycle no event breaks, so the instance would go round it "
        + "forever. Let an event, not a timer, lead out of one of these states.";

    private static int IndexOf<TState>(IReadOnlyList<TState> order, TState state)
    {
        for (var i = 0; i < order.Count; i++)
            if (EqualityComparer<TState>.Default.Equals(order[i], state))
                return i;
        return order.Count;
    }
}
