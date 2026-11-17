using Trax.Core.Train;

namespace Trax.Core.Monad;

/// <summary>
/// What a <c>Parallel</c> step does to its other branches when one of them fails.
/// </summary>
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
