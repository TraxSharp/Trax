using Trax.Core.Decisions;
using Trax.Core.Functional;

namespace Trax.Core.Monad;

/// <summary>
/// Where a resume walk is: the point it skips to, whether it has reached it, and what each branch
/// restores.
/// </summary>
internal sealed class ResumeWalk(
    string? target,
    bool inclusive,
    IReadOnlyCollection<Type> restores,
    IReadOnlyDictionary<string, (string Checkpoint, IReadOnlyList<Type> Restores)> branches
)
{
    /// <summary>What the checkpoint this walk starts from restores, as it was when it was taken.</summary>
    public System.Collections.Generic.HashSet<Type> Restores { get; } = [.. restores];

    /// <summary>The types a skipped step wrote that nothing restores.</summary>
    public System.Collections.Generic.HashSet<Type> Stale { get; } = [];

    /// <summary>True once a step from the point on read a stale type, here or in a branch.</summary>
    public bool ReadStale { get; private set; }

    /// <summary>The walk a branch's walk started from, which learns that a branch read a stale type.</summary>
    private ResumeWalk? _parent;

    /// <summary>Whether <paramref name="type"/> is stale, noting that a step read it.</summary>
    public bool ReadsStale(Type type)
    {
        if (!Stale.Contains(type))
            return false;

        for (var walk = this; walk is not null; walk = walk._parent)
            walk.ReadStale = true;

        return true;
    }

    public string Target { get; } = target ?? "";

    public bool Inclusive { get; } = inclusive;

    public bool Skipping { get; set; } = target is not null;

    /// <summary>
    /// The walk of the branch at <paramref name="path"/>: from its own checkpoint when it has one,
    /// whose state and routes go into <paramref name="memory"/>, else from its start. A type a
    /// skipped step before the <c>Parallel</c> overwrote is stale in the branch too, unless the
    /// branch's own checkpoint holds it.
    /// </summary>
    public ResumeWalk ForBranch(string path, System.Collections.Generic.HashSet<Type> memory)
    {
        ResumeWalk walk;

        if (branches.TryGetValue(path, out var restored))
        {
            memory.UnionWith(restored.Restores);
            walk = new ResumeWalk(
                restored.Checkpoint,
                inclusive: true,
                restored.Restores,
                branches
            );
        }
        else
            walk = new ResumeWalk(target: null, inclusive: false, [], branches);

        walk._parent = this;
        walk.Stale.UnionWith(Stale.Where(t => !walk.Restores.Contains(t)));
        return walk;
    }
}
