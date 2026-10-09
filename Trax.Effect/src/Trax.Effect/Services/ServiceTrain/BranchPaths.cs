using Trax.Core.Monad;

namespace Trax.Effect.Services.ServiceTrain;

/// <summary>
/// The <c>Parallel</c> branch a step runs in, as Trax.Core names it, and how two branches of one
/// run stand to each other. A run's state that follows its path (the track it is on, whether it
/// withholds, which decision a routing step routes on) is kept per branch with these, because the
/// branches of one run run side by side on the same run.
/// </summary>
/// <remarks>
/// <para>A branch's path starts with the path of the branch it is declared in, so
/// <c>Parallel#0/a/Parallel#0/x</c> is inside <c>Parallel#0/a</c>, and every branch is inside the
/// run's own chain, whose path is <see cref="Run"/>.</para>
/// <para>Two facts make per-branch state simple. A chain waits at the join while its branches run,
/// so what a branch inherits from the chain it was forked from cannot change under it: it is read
/// from that chain's state rather than copied. And by the time a chain runs its next step, every
/// branch inside it has joined, so what those branches did is finished.</para>
/// </remarks>
internal static class BranchPaths
{
    /// <summary>The path of the run's own steps, outside any branch.</summary>
    public const string Run = "";

    /// <summary>The path of the branch the step on this async flow runs in, or <see cref="Run"/>.</summary>
    public static string Current => ChainGraph.CurrentBranchPath ?? Run;

    /// <summary>
    /// Whether <paramref name="inner"/> is <paramref name="outer"/> or a branch inside it.
    /// </summary>
    public static bool Encloses(string outer, string inner) =>
        outer.Length == 0
        || inner == outer
        || (
            inner.Length > outer.Length
            && inner[outer.Length] == '/'
            && inner.StartsWith(outer, StringComparison.Ordinal)
        );

    /// <summary>
    /// Every path that <see cref="Encloses"/> <paramref name="path"/>: the run's own, each branch
    /// it is inside, and itself, shortest first.
    /// </summary>
    public static List<string> Enclosing(string path)
    {
        var paths = new List<string> { Run };

        for (var i = path.IndexOf('/'); i >= 0; i = path.IndexOf('/', i + 1))
            paths.Add(path[..i]);

        if (path.Length > 0)
            paths.Add(path);

        return paths;
    }

    /// <summary>
    /// Whether the branches <paramref name="a"/> and <paramref name="b"/> run side by side: they
    /// part at the same <c>Parallel</c> step, into different branches of it. Otherwise one of
    /// them encloses the other, or they part at different steps of one chain, which ran one
    /// after the other.
    /// </summary>
    /// <remarks>
    /// A path is a step id and a name, repeated: a <c>Parallel</c> step and one of its branches,
    /// or a routing step and one of its tracks. Two paths that first differ in a step id part at
    /// two steps of one chain; two that first differ in a name part at one step.
    /// </remarks>
    public static bool Alongside(string a, string b)
    {
        var left = a.Length == 0 ? [] : a.Split('/');
        var right = b.Length == 0 ? [] : b.Split('/');

        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
            if (left[i] != right[i])
                return i % 2 == 1;

        return false;
    }
}
