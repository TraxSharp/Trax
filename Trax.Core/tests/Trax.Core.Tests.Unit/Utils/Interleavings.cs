using System.Globalization;
using Trax.Core.Monad;

namespace Trax.Core.Tests.Unit.Utils;

/// <summary>
/// Enumerates every interleaving a <see cref="ScheduledBranchRunner"/> can produce for one run,
/// by replaying it with every sequence of choices: a depth-first search over the choice points,
/// where each run follows a prefix and then always takes the first candidate.
/// </summary>
internal static class Interleavings
{
    public sealed record Explored<T>(
        IReadOnlyList<int> Choices,
        ScheduledBranchRunner Runner,
        T Result
    );

    public static async Task<List<Explored<T>>> Every<T>(
        Func<ScheduledBranchRunner, Task<T>> run,
        Action<string, int>? atJunction = null
    )
    {
        var explored = new List<Explored<T>>();
        var pending = new Stack<int[]>();
        pending.Push([]);

        while (pending.Count > 0)
        {
            var prefix = pending.Pop();
            var taken = new List<int>();
            var options = new List<int>();

            var runner = new ScheduledBranchRunner(
                ready =>
                {
                    var choice = taken.Count < prefix.Length ? prefix[taken.Count] : 0;
                    taken.Add(choice);
                    options.Add(ready.Count);
                    return choice;
                },
                atJunction
            );

            var result = await run(runner).ConfigureAwait(false);
            explored.Add(new Explored<T>(taken, runner, result));

            for (var point = prefix.Length; point < taken.Count; point++)
            for (var other = 1; other < options[point]; other++)
                pending.Push([.. taken.Take(point), other]);
        }

        return explored;
    }
}
