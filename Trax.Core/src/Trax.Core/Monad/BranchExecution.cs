namespace Trax.Core.Monad;

/// <summary>
/// How a <c>Parallel</c> step's branches are run relative to each other.
/// </summary>
internal enum BranchExecution
{
    /// <summary>Every branch starts at once. What a shipped host always does.</summary>
    Concurrent,

    /// <summary>
    /// One branch after another, in declared order, each finishing before the next starts. The
    /// oracle the concurrency tests compare a concurrent run against: whatever the branches do
    /// side by side has to be explainable as something they could have done one at a time.
    /// </summary>
    Sequential,
}
