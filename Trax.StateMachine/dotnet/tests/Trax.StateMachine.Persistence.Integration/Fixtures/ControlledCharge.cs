using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;

namespace Trax.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// The order machines' charge, scripted run by run: each run takes the next of <see cref="Script"/> (then
/// <see cref="ChargeRun.Succeed"/>), and records the key it was given and the receipt it returned.
/// </summary>
public sealed class ControlledCharge : IOrderCharge
{
    private readonly ConcurrentQueue<ChargeRun> _script = new();
    private TaskCompletionSource _entered = NewSignal();
    private TaskCompletionSource _released = NewSignal();
    private int _calls;

    /// <summary>The key each run was given, in order.</summary>
    public ConcurrentQueue<string> Keys { get; } = new();

    /// <summary>Every receipt a run returned, with the key it ran under.</summary>
    public ConcurrentQueue<(string Key, string Receipt)> Receipts { get; } = new();

    /// <summary>How many times the charge ran.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Completes when a held run has started.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Queues what the next runs do.</summary>
    public void Script(params ChargeRun[] runs)
    {
        foreach (var run in runs)
            _script.Enqueue(run);
    }

    /// <summary>Forgets every queued run, so the next runs succeed until scripted again.</summary>
    public void Clear()
    {
        while (_script.TryDequeue(out _)) { }
    }

    /// <summary>The keys this charge ran under for the instance <paramref name="id"/>.</summary>
    public IEnumerable<string> KeysOf(Guid id) =>
        Keys.Where(k => k.Contains(id.ToString(), StringComparison.Ordinal));

    /// <summary>Lets the held run finish, and arms the next hold.</summary>
    public void Release()
    {
        var released = _released;
        _entered = NewSignal();
        _released = NewSignal();
        released.TrySetResult();
    }

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
        Run(snapshot, "", cancellationToken);

    public async Task<string> Run(
        Snapshot snapshot,
        string effectKey,
        CancellationToken cancellationToken
    )
    {
        var n = Interlocked.Increment(ref _calls);
        Keys.Enqueue(effectKey);
        var run = _script.TryDequeue(out var next) ? next : ChargeRun.Succeed;
        var released = _released.Task;
        if (run is ChargeRun.Hold or ChargeRun.HoldThenFail)
        {
            _entered.TrySetResult();
            await released.ConfigureAwait(false);
        }
        switch (run)
        {
            case ChargeRun.Fail or ChargeRun.HoldThenFail:
                throw new InvalidOperationException("The charge was declined.");
            case ChargeRun.TimeOut:
                throw new TaskCanceledException("The charge timed out.");
        }
        var receipt = string.Create(CultureInfo.InvariantCulture, $"receipt-{n}");
        Receipts.Enqueue((effectKey, receipt));
        return receipt;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
