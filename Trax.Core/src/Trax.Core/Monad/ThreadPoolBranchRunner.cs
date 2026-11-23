namespace Trax.Core.Monad;

/// <summary>
/// Runs every branch at once on the thread pool, so a junction that blocks its thread holds up
/// only its own branch.
/// </summary>
internal sealed class ThreadPoolBranchRunner : IBranchRunner
{
    public static readonly ThreadPoolBranchRunner Instance = new();

    private ThreadPoolBranchRunner() { }

    public BranchExecution Execution => BranchExecution.Concurrent;

    public Task Start(string branch, Func<Task> run) => Task.Run(run, CancellationToken.None);

    public Task BeforeJunction(string branch, string node) => Task.CompletedTask;

    public Task Join(string? parent, Task branches) => branches;
}
