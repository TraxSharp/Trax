using Trax.Core.Train;

namespace Trax.Core.Monad;

/// <summary>
/// Declares the branches of a <c>Parallel</c> step: a fixed set of chains that run side by side
/// within one run, each on its own copy of Memory, and join before the step after it.
/// </summary>
/// <remarks>
/// <para>Each branch is written the way the rest of the chain is, on the parameter it is handed
/// (<c>b =&gt; b.Chain&lt;EmbeddingSimilarity&gt;()</c>), and is read when the chain is declared
/// and verified at startup. Every branch starts at once.</para>
/// <para>A branch is named, and the name is part of the id of every step in it, so it stays the
/// same when another branch is added. Names are unique within the step.</para>
/// <para>Branches compute; the step after the join commits. Each branch has its own dependency
/// injection scope, so its writes are not in the same transaction as its siblings'.</para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/train-methods/parallel">SDK reference</seealso>
public sealed class Branches<TInput, TReturn>
{
    internal List<DeclaredBranch<TInput, TReturn>> Declared { get; } = [];

    internal List<string> Problems { get; } = [];

    internal BranchFailurePolicy Policy { get; private set; } = BranchFailurePolicy.CancelSiblings;

    /// <summary>Declares a branch named <paramref name="name"/>.</summary>
    /// <param name="name">The branch's name, unique within the step. Part of each of its steps' ids.</param>
    /// <param name="body">The branch's chain.</param>
    public Branches<TInput, TReturn> Branch(
        string name,
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> body
    )
    {
        ArgumentNullException.ThrowIfNull(body);

        var problems = Problems.Count;

        if (string.IsNullOrWhiteSpace(name))
            Problems.Add("declares a branch with no name. Name every branch.");
        else if (
            name.Contains('/', StringComparison.Ordinal)
            || name.Contains('#', StringComparison.Ordinal)
        )
            Problems.Add(
                $"names a branch '{name}', and a branch name cannot contain '/' or '#': they "
                    + "separate the parts of a step's id."
            );
        else if (Declared.Exists(b => string.Equals(b.Name, name, StringComparison.Ordinal)))
            Problems.Add($"declares the branch '{name}' twice. Name each branch once.");

        Declared.Add(
            new DeclaredBranch<TInput, TReturn>(name ?? "", body)
            {
                Refused = Problems.Count > problems,
            }
        );
        return this;
    }

    /// <summary>
    /// What to do with the other branches when one fails. Defaults to
    /// <see cref="BranchFailurePolicy.CancelSiblings"/>.
    /// </summary>
    public Branches<TInput, TReturn> OnFailure(BranchFailurePolicy policy)
    {
        Policy = policy;
        return this;
    }

    internal IEnumerable<string> AllProblems =>
        Declared.Count == 0
            ? Problems.Append("declares no branches. Add one with Branch.")
            : Problems;
}
