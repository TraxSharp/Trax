using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>What a <see cref="ControlledCharge"/> does on one run.</summary>
public enum ChargeRun
{
    /// <summary>Returns a fresh receipt at once.</summary>
    Succeed,

    /// <summary>Waits until <see cref="ControlledCharge.Release"/>, then returns a fresh receipt.</summary>
    Hold,

    /// <summary>Waits until <see cref="ControlledCharge.Release"/>, then throws: the charge did not happen.</summary>
    HoldThenFail,

    /// <summary>Throws at once: the charge did not happen.</summary>
    Fail,

    /// <summary>Throws a cancellation at once: whether the charge happened is unknown.</summary>
    TimeOut,
}

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
            await released;
        }
        switch (run)
        {
            case ChargeRun.Fail or ChargeRun.HoldThenFail:
                throw new InvalidOperationException("The charge was declined.");
            case ChargeRun.TimeOut:
                throw new TaskCanceledException("The charge timed out.");
        }
        var receipt = $"receipt-{n}";
        Receipts.Enqueue((effectKey, receipt));
        return receipt;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Drives a draft service on an <see cref="InstanceHost"/>, each call in a scope of its own.</summary>
internal static class Drive
{
    public static async Task<Guid> Start(InstanceHost host, Actor.Client actor, string machine)
    {
        using var scope = host.Scope();
        var started = await host.Service(scope, machine)
            .Start(actor, new JsonObject { ["title"] = "t" }, $"start-{Guid.NewGuid():N}");
        return started.Should().BeOfType<StartOutcome.Started>().Which.Id;
    }

    public static async Task<StartOutcome> TryStart(
        InstanceHost host,
        Actor.Client actor,
        string machine
    )
    {
        using var scope = host.Scope();
        return await host.Service(scope, machine)
            .Start(actor, new JsonObject { ["title"] = "t" }, $"start-{Guid.NewGuid():N}");
    }

    public static Task<AdvanceOutcome> Event(
        InstanceHost host,
        Actor.Client actor,
        string machine,
        Guid id,
        string trigger,
        JsonNode? input = null
    ) =>
        Task.Run(async () =>
        {
            using var scope = host.Scope();
            return await host.Service(scope, machine)
                .Advance(actor, id, trigger, input, $"e-{Guid.NewGuid():N}");
        });

    public static Task<AdvanceOutcome> Send(
        InstanceHost host,
        Actor.Client actor,
        string machine,
        Guid id,
        string? requestId = null
    ) =>
        Task.Run(async () =>
        {
            using var scope = host.Scope();
            return await host.Service(scope, machine)
                .Send(actor, id, requestId ?? $"s-{Guid.NewGuid():N}");
        });

    public static async Task<DiscardOutcome> Discard(
        InstanceHost host,
        Actor.Client actor,
        string machine,
        Guid id
    )
    {
        using var scope = host.Scope();
        return await host.Service(scope, machine).Discard(actor, id, $"d-{Guid.NewGuid():N}");
    }

    public static Task<IReadOnlyList<TimerFireResult>> SweepTimers(InstanceHost host) =>
        host
            .Services.GetRequiredService<InvokeOutcomeReconciler>()
            .FireDueTimersOnce(CancellationToken.None);

    public static Task<int> SweepDrafts(InstanceHost host) =>
        host.Services.GetRequiredService<DraftSweeper>().SweepOnce();
}
