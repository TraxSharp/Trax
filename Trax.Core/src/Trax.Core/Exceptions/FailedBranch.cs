using Trax.Core.Monad;

namespace Trax.Core.Exceptions;

/// <summary>
/// One branch of a <c>Parallel</c> step that failed. Only the step that failed makes one.
/// </summary>
public sealed record FailedBranch
{
    internal FailedBranch(string branch, Exception exception, FailureClass? failureClass)
    {
        Branch = branch;
        Exception = exception;
        FailureClass = failureClass;
    }

    /// <summary>The branch's path: the step's id and the branch's name, as in <c>Parallel#0/cocitation</c>.</summary>
    public string Branch { get; }

    /// <summary>What the branch failed with.</summary>
    public Exception Exception { get; }

    /// <summary>The class the failure carries from where it happened, or null when it carries none.</summary>
    public FailureClass? FailureClass { get; }
}
