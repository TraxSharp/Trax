using System.Diagnostics.CodeAnalysis;
using Trax.Core.Monad;

namespace Trax.Core.Exceptions;

/// <summary>
/// One branch of a <c>Parallel</c> step that failed. Only the step that failed makes one.
/// </summary>
[Experimental(ExperimentalIds.Parallel)]
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

/// <summary>
/// A <c>Parallel</c> step failed: one or more of its branches did. The run fails with this one
/// exception, which carries every branch that failed.
/// </summary>
/// <remarks>
/// <para>Its <see cref="TrainExceptionData"/> names the first branch to fail and carries the
/// combined <see cref="Exceptions.FailureClass"/> (<see cref="Combine"/>): the step can be retried
/// as a whole only when every failure can.</para>
/// <para>A branch that failed after a sibling's failure cancelled it, under
/// <see cref="BranchFailurePolicy.CancelSiblings"/>, is not a failure, whatever it threw, and is
/// listed in <see cref="CancelledBySibling"/> instead. A run that was cancelled fails with the cancellation,
/// never with this.</para>
/// </remarks>
[Experimental(ExperimentalIds.Parallel)]
public sealed class BranchesFailedException : TrainException
{
    /// <summary>Creates the failure of a <c>Parallel</c> step. Only the step that failed makes one.</summary>
    /// <param name="step">The step's id.</param>
    /// <param name="failures">The branches that failed, first to fail first. At least one.</param>
    /// <param name="cancelledBySibling">The paths of the branches stopped because a sibling failed.</param>
    internal BranchesFailedException(
        string step,
        IReadOnlyList<FailedBranch> failures,
        IReadOnlyList<string> cancelledBySibling
    )
        : base(Describe(step, failures))
    {
        ArgumentOutOfRangeException.ThrowIfZero(failures.Count);

        Step = step;
        Failures = failures;
        CancelledBySibling = cancelledBySibling;
    }

    /// <summary>The <c>Parallel</c> step's id.</summary>
    public string Step { get; }

    /// <summary>
    /// The branches that failed, in the order the step recorded their failures. Branches that
    /// fail at the same moment are ordered as the step happened to see them, so only the first
    /// is meaningful when failures race.
    /// </summary>
    public IReadOnlyList<FailedBranch> Failures { get; }

    /// <summary>The paths of the branches stopped because a sibling failed.</summary>
    public IReadOnlyList<string> CancelledBySibling { get; }

    /// <summary>
    /// The class of several failures together: a join over
    /// <c>Transient &lt; Conflict &lt; Unclassified &lt; Permanent</c>, with a missing class
    /// counted as <see cref="FailureClass.Unclassified"/>.
    /// </summary>
    /// <remarks>
    /// The combined failure is retryable only when every part is: one permanent failure makes the
    /// whole permanent, and one nobody classified leaves the whole unclassified rather than
    /// claiming it transient. The operation is associative, commutative and idempotent, so the
    /// order the branches failed in does not change the answer.
    /// </remarks>
    public static FailureClass Combine(IEnumerable<FailureClass?> classes) =>
        classes.Select(c => c ?? FailureClass.Unclassified).Aggregate(FailureClass.Transient, Join);

    private static FailureClass Join(FailureClass a, FailureClass b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(FailureClass c) =>
        c switch
        {
            FailureClass.Transient => 0,
            FailureClass.Conflict => 1,
            FailureClass.Permanent => 3,
            _ => 2,
        };

    private static string Describe(string step, IReadOnlyList<FailedBranch> failures) =>
        failures.Count == 1
            ? $"{step}: branch '{failures[0].Branch}' failed: {failures[0].Exception.Message}"
            : $"{step}: {failures.Count} branches failed: "
                + string.Join("; ", failures.Select(f => $"'{f.Branch}': {f.Exception.Message}"));
}
