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

/// <summary>
/// Runs a <c>Parallel</c> step's branches one step at a time, in an order a test chooses. Only
/// one branch runs at once: it holds the baton from when it is let go until it reaches its next
/// junction, waits on a nested <c>Parallel</c>, or finishes, and then the chooser picks which
/// waiting branch goes next. Every interleaving of junction steps is one sequence of choices, so
/// a test can enumerate them, or pick them at random from a seed, and replay any of them.
/// </summary>
/// <remarks>
/// Nothing here sleeps or polls: a branch waits on its own signal, and a branch is only let go
/// once every branch that could run has stopped at a hold, so the choice is made between the
/// same candidates every time the same choices are replayed.
/// </remarks>
/// <param name="choose">Given the branches waiting to go, in the order they began waiting, the index of the one to let go.</param>
/// <param name="atJunction">As for <see cref="SequentialBranchRunner"/>, called when a branch is let go into a junction.</param>
internal sealed class ScheduledBranchRunner(
    Func<IReadOnlyList<string>, int> choose,
    Action<string, int>? atJunction = null
) : IBranchRunner
{
    private readonly Lock _gate = new();
    private readonly List<(string Branch, TaskCompletionSource Go)> _ready = [];
    private readonly List<string> _started = [];
    private readonly Dictionary<string, Group> _groupOf = [];
    private readonly Dictionary<string, int> _junctions = [];
    private readonly List<string> _trace = [];
    private readonly List<string> _finished = [];
    private bool _held;

    private sealed class Group(string? parent, int remaining)
    {
        public string? Parent { get; } = parent;
        public int Remaining { get; set; } = remaining;
        public TaskCompletionSource? ParentGo { get; set; }
    }

    public BranchExecution Execution => BranchExecution.Concurrent;

    /// <summary>What ran, in order: <c>branch^</c> for a start, <c>branch#k</c> for a junction, <c>branch$</c> for an end.</summary>
    public IReadOnlyList<string> Trace
    {
        get
        {
            lock (_gate)
                return [.. _trace];
        }
    }

    /// <summary>The branches that finished, in the order they did.</summary>
    public IReadOnlyList<string> Finished
    {
        get
        {
            lock (_gate)
                return [.. _finished];
        }
    }

    public Task Start(string branch, Func<Task> run)
    {
        var go = Signal();
        lock (_gate)
        {
            _ready.Add((branch, go));
            _started.Add(branch);
        }

        return Body();

        async Task Body()
        {
            await go.Task.ConfigureAwait(false);
            lock (_gate)
                _trace.Add($"{branch}^");

            try
            {
                await run().ConfigureAwait(false);
            }
            finally
            {
                Finish(branch);
            }
        }
    }

    public async Task BeforeJunction(string branch, string node)
    {
        var go = Signal();
        int index;
        lock (_gate)
        {
            index = _junctions[branch] = _junctions.GetValueOrDefault(branch, -1) + 1;
            _ready.Add((branch, go));
            _held = false;
        }

        Dispatch();
        await go.Task.ConfigureAwait(false);

        lock (_gate)
            _trace.Add($"{branch}#{index}");

        atJunction?.Invoke(branch, index);
    }

    public async Task Join(string? parent, Task branches)
    {
        TaskCompletionSource? go = null;
        lock (_gate)
        {
            // Every branch of this step was started just now, by the caller, before it joined.
            var group = new Group(parent, _started.Count);
            foreach (var branch in _started)
                _groupOf[branch] = group;
            _started.Clear();

            if (parent is not null)
            {
                go = group.ParentGo = Signal();
                _held = false;
            }
        }

        Dispatch();
        await branches.ConfigureAwait(false);

        if (go is not null)
            await go.Task.ConfigureAwait(false);
    }

    private void Finish(string branch)
    {
        lock (_gate)
        {
            _trace.Add($"{branch}$");
            _finished.Add(branch);
            var group = _groupOf[branch];

            // The parent becomes a candidate the moment its last branch ends, not when its await
            // resumes, so the next choice is made between the same candidates on every replay.
            if (--group.Remaining == 0 && group.ParentGo is { } parentGo)
                _ready.Add((group.Parent!, parentGo));

            _held = false;
        }

        Dispatch();
    }

    private void Dispatch()
    {
        TaskCompletionSource go;
        lock (_gate)
        {
            if (_held || _ready.Count == 0)
                return;

            var at = choose(_ready.Select(r => r.Branch).ToList());
            go = _ready[at].Go;
            _ready.RemoveAt(at);
            _held = true;
        }

        go.SetResult();
    }

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Enumerates every interleaving a <see cref="ScheduledBranchRunner"/> can produce for one run,
/// by replaying it with every sequence of choices: a depth-first search over the choice points,
/// where each run follows a prefix and then always takes the first candidate.
/// </summary>
internal static class Interleavings
{
    public sealed record Explored<T>(
        IReadOnlyList<int> Choices,
        ScheduledBranchRunner Runner,
        T Result
    );

    public static async Task<List<Explored<T>>> Every<T>(
        Func<ScheduledBranchRunner, Task<T>> run,
        Action<string, int>? atJunction = null
    )
    {
        var explored = new List<Explored<T>>();
        var pending = new Stack<int[]>();
        pending.Push([]);

        while (pending.Count > 0)
        {
            var prefix = pending.Pop();
            var taken = new List<int>();
            var options = new List<int>();

            var runner = new ScheduledBranchRunner(
                ready =>
                {
                    var choice = taken.Count < prefix.Length ? prefix[taken.Count] : 0;
                    taken.Add(choice);
                    options.Add(ready.Count);
                    return choice;
                },
                atJunction
            );

            var result = await run(runner).ConfigureAwait(false);
            explored.Add(new Explored<T>(taken, runner, result));

            for (var point = prefix.Length; point < taken.Count; point++)
            for (var other = 1; other < options[point]; other++)
                pending.Push([.. taken.Take(point), other]);
        }

        return explored;
    }
}
