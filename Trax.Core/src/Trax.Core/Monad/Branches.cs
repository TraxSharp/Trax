using System.Diagnostics.CodeAnalysis;
using Trax.Core.Train;

namespace Trax.Core.Monad;

/// <summary>
/// What a <c>Parallel</c> step does to its other branches when one of them fails.
/// </summary>
[Experimental(ExperimentalIds.Parallel)]
public enum BranchFailurePolicy
{
    /// <summary>
    /// Cancel the other branches as soon as one fails, and fail the step once they have stopped.
    /// The default: a failed step is retried as a whole, so finishing the siblings' work is waste.
    /// </summary>
    CancelSiblings,

    /// <summary>
    /// Let every branch finish, then fail the step with every branch that failed. For branches
    /// whose own work is worth finishing, or whose failures are all worth seeing at once.
    /// </summary>
    WaitForAll,
}

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
[Experimental(ExperimentalIds.Parallel)]
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

        if (string.IsNullOrWhiteSpace(name))
            Problems.Add("declares a branch with no name. Name every branch.");
        else if (name.Contains('/') || name.Contains('#'))
            Problems.Add(
                $"names a branch '{name}', and a branch name cannot contain '/' or '#': they "
                    + "separate the parts of a step's id."
            );
        else if (Declared.Any(b => b.Name == name))
            Problems.Add($"declares the branch '{name}' twice. Name each branch once.");

        Declared.Add(new DeclaredBranch<TInput, TReturn>(name ?? "", body));
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

/// <summary>
/// The diagnostic ids experimental features ship under, one per feature. Using one reports its id
/// as an error until the feature's row of the interaction matrix is complete; a project that opts
/// in suppresses it.
/// </summary>
internal static class ExperimentalIds
{
    /// <summary><c>Parallel</c>: branches that run side by side within one run.</summary>
    public const string Parallel = "TRAXEXP001";

    // TRAXEXP002 (Invokes, Trax.Docs/adr/0046) and TRAXEXP003 (Checkpoint, Trax.Docs/adr/0047) were
    // lifted once their rows of the interaction matrix were complete. Ids are never reused.
}

/// <summary>One declared branch: its name and its chain.</summary>
internal sealed record DeclaredBranch<TInput, TReturn>(
    string Name,
    Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> Body
);

/// <summary>
/// The branch running on this async flow: the token its junctions honour and the path that
/// names it. Unset outside a branch.
/// </summary>
internal static class RunningBranch
{
    private static readonly AsyncLocal<(object Train, CancellationToken Token)?> Current = new();

    /// <summary>
    /// The branch's token for a junction of <paramref name="train"/>, or null outside a branch of
    /// it. A train run inside a branch's junction keeps its own token.
    /// </summary>
    public static CancellationToken? TokenFor(object train) =>
        Current.Value is { } branch && ReferenceEquals(branch.Train, train) ? branch.Token : null;

    public static void Enter(object train, CancellationToken token) =>
        Current.Value = (train, token);
}
