using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using CsCheck;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.Utils;

/// <summary>
/// How a run ended, in the terms the concurrency laws compare: the value or the failure, and for
/// a failed <c>Parallel</c> the branches that failed and the ones stopped by a sibling, read
/// through every nested <c>Parallel</c>.
/// </summary>
internal sealed record Outcome(Either<Exception, string> Result, string? Snapshot)
{
    public bool IsRight => Result.IsRight;

    public Exception? Failure => Result.IsLeft ? Result.Swap().ValueUnsafe() : null;

    public bool Cancelled => Failure is OperationCanceledException;

    /// <summary>The paths of the branches the outermost failed <c>Parallel</c> reports as failed.</summary>
    public IReadOnlySet<string> FailedBranches =>
        Failure is BranchesFailedException f
            ? f.Failures.Select(b => b.Branch).ToHashSet(StringComparer.Ordinal)
            : [];

    /// <summary>The paths of every branch a sibling's failure stopped, at any depth.</summary>
    public IReadOnlySet<string> Stopped
    {
        get
        {
            var stopped = new HashSet<string>(StringComparer.Ordinal);
            Walk(Failure);
            return stopped;

            void Walk(Exception? e)
            {
                if (e is not BranchesFailedException f)
                    return;
                stopped.UnionWith(f.CancelledBySibling);
                foreach (var failed in f.Failures)
                    Walk(failed.Exception);
            }
        }
    }

    /// <summary>
    /// Every failure at the bottom of the failure, with the path of the branch it happened in
    /// (empty outside every branch): a failed <c>Parallel</c> is read through to what its branches
    /// failed with.
    /// </summary>
    public IReadOnlySet<(string Branch, string Message)> Leaves
    {
        get
        {
            var leaves = new HashSet<(string, string)>();
            if (Failure is { } failure)
                Walk(failure, "");
            return leaves;

            void Walk(Exception e, string path)
            {
                if (e is BranchesFailedException f)
                    foreach (var failed in f.Failures)
                        Walk(failed.Exception, failed.Branch);
                else
                    leaves.Add((path, e.Message));
            }
        }
    }

    public static async Task<Outcome> Of(Train<string, string> train, string input)
    {
        var result = await train.RunEither(input).ConfigureAwait(false);
        return new Outcome(result, (train as ChainShapes.ShapeTrain)?.Snapshot);
    }

    public override string ToString() =>
        IsRight
            ? $"Right({Snapshot ?? Result.ValueUnsafe()})"
            : $"Left({Failure!.GetType().Name}: failed [{string.Join(", ", FailedBranches.Order(StringComparer.Ordinal))}], "
                + $"stopped [{string.Join(", ", Stopped.Order(StringComparer.Ordinal))}], "
                + $"leaves [{string.Join(", ", Leaves.Select(l => $"{l.Branch}:{l.Message}").Order(StringComparer.Ordinal))}])";
}
