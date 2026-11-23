using System.Globalization;
using Trax.Core.Monad;

namespace Trax.Core.Tests.Unit.Utils;

/// <summary>
/// Runs a <c>Parallel</c> step's branches one after another in declared order, each to its end
/// before the next starts, on the caller's thread. The oracle a concurrent run is compared with.
/// </summary>
/// <param name="atJunction">
/// Called before each junction a branch runs, with the branch's path and the junction's index in
/// that branch from 0. What it throws fails the branch there; it may also cancel the run.
/// </param>
internal sealed class SequentialBranchRunner(Action<string, int>? atJunction = null) : IBranchRunner
{
    private readonly Dictionary<string, int> _junctions = [];

    public BranchExecution Execution => BranchExecution.Sequential;

    public Task Start(string branch, Func<Task> run) => run();

    public Task BeforeJunction(string branch, string node)
    {
        int index;
        lock (_junctions)
            index = _junctions[branch] = _junctions.GetValueOrDefault(branch, -1) + 1;

        atJunction?.Invoke(branch, index);
        return Task.CompletedTask;
    }

    public Task Join(string? parent, Task branches) => branches;
}
