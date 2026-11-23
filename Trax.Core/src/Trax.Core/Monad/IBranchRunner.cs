namespace Trax.Core.Monad;

/// <summary>
/// Starts a <c>Parallel</c> step's branches and waits for them. A run uses
/// <see cref="ThreadPoolBranchRunner"/>; the seam exists so a test can control the interleaving
/// of branches, which the thread pool leaves to chance.
/// </summary>
/// <remarks>
/// Internal on purpose: a consumer has no reason to run branches any other way, and a runner that
/// got the contract wrong (a branch started twice, a join that returns early) would corrupt the
/// run's Memory rather than fail it.
/// </remarks>
internal interface IBranchRunner
{
    /// <summary>Whether branches start at once or one after another.</summary>
    BranchExecution Execution { get; }

    /// <summary>
    /// Starts one branch. <paramref name="run"/> never throws: a branch's failure is recorded,
    /// not raised. Under <see cref="BranchExecution.Sequential"/> the step awaits each branch
    /// before starting the next.
    /// </summary>
    /// <param name="branch">The branch's path, as in <c>Parallel#0/cocitation</c>.</param>
    /// <param name="run">Runs the branch to its end.</param>
    Task Start(string branch, Func<Task> run);

    /// <summary>
    /// Called in a branch before each junction it runs. A test runner can hold the branch here
    /// until it chooses to let it go on. What this throws fails the branch at this junction, as
    /// though the junction had thrown it.
    /// </summary>
    /// <param name="branch">The path of the branch about to run a junction.</param>
    /// <param name="node">The junction's node id.</param>
    Task BeforeJunction(string branch, string node);

    /// <summary>
    /// Waits for every branch of one step to finish.
    /// </summary>
    /// <param name="parent">The path of the branch whose chain holds the step, or null when the run's own chain does.</param>
    /// <param name="branches">Completes when every branch of the step has finished.</param>
    Task Join(string? parent, Task branches);
}
