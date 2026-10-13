using System.Text.Json.Nodes;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

public enum TimedChargeState
{
    Review,
    Charged,
    Expired,
}

public enum TimedChargeTrigger
{
    Charge,
}

/// <summary>
/// A user's machine whose timed <c>Review</c> is also left by an irreversible effect: the effect's target and the
/// timer race for the same row.
/// </summary>
public sealed class TimedChargeMachine : Machine<TimedChargeState, TimedChargeTrigger>
{
    public const string Id = "timed-charge";

    protected override void Configure(IMachineBuilder<TimedChargeState, TimedChargeTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(TimedChargeState.Review, () => new JsonObject());
        m.In(TimedChargeState.Review)
            .After(TimeSpan.FromMinutes(10))
            .GoTo(TimedChargeState.Expired)
            .On(TimedChargeTrigger.Charge)
            .RunsOnce<IOrderCharge>($"{Id}:charge")
            .Reduce((_, input) => new JsonObject { ["receipt"] = input?["receipt"]?.DeepClone() })
            .To(TimedChargeState.Charged);
        m.In(TimedChargeState.Charged);
        m.In(TimedChargeState.Expired);
    }

    public static string Json(string state) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = new JsonObject(),
        }.ToJsonString();
}

public enum QueueingTimerState
{
    Idle,
    Waiting,
    Building,
    Built,
    Failed,
    Cancelled,
}

public enum QueueingTimerTrigger
{
    Open,
}

/// <summary>A system machine whose timer enters an invoking state, so firing it queues a run.</summary>
public sealed class QueueingTimerMachine : Machine<QueueingTimerState, QueueingTimerTrigger>
{
    public const string Id = "queueing-timer";

    protected override void Configure(IMachineBuilder<QueueingTimerState, QueueingTimerTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(QueueingTimerState.Idle, () => new JsonObject { ["source"] = "repo" });
        m.In(QueueingTimerState.Idle)
            .Context<BuildMachine.SourceContext>()
            .On(QueueingTimerTrigger.Open)
            .To(QueueingTimerState.Waiting);
        m.In(QueueingTimerState.Waiting)
            .Context<BuildMachine.SourceContext>()
            .After(TimeSpan.FromMinutes(1))
            .GoTo(QueueingTimerState.Building);
        m.In(QueueingTimerState.Building)
            .Context<BuildMachine.SourceContext>()
            .Invokes<IBuildTrain, BuildInput, BuildOutput>(ctx => new BuildInput(
                ctx["source"]!.GetValue<string>()
            ))
            .OnDone(QueueingTimerState.Built)
            .OnFailed(QueueingTimerState.Failed)
            .OnCancelled(QueueingTimerState.Cancelled);
        m.In(QueueingTimerState.Built).Context<BuildMachine.SourceContext>();
        m.In(QueueingTimerState.Failed).Context<BuildMachine.SourceContext>();
        m.In(QueueingTimerState.Cancelled).Context<BuildMachine.SourceContext>();
        m.SystemOwned();
    }
}

/// <summary>
/// A launcher whose enqueue throws for <see cref="QueueingTimerMachine"/>, as the mediator's does for an input it
/// cannot store, and records every other launch.
/// </summary>
internal sealed class ThrowingLauncher : IInvokedTrainLauncher
{
    private readonly RecordingLauncher _inner = new();

    public int Throws => Volatile.Read(ref _throws);

    private int _throws;

    /// <summary>What the enqueue throws; an <see cref="InvalidOperationException"/> unless a test sets it.</summary>
    public Func<Exception> Failure { get; set; } =
        () => new InvalidOperationException("The run's input could not be stored.");

    public IReadOnlyList<string> Refusals(
        InvokedTrainDeclaration declaration,
        IServiceProvider services
    ) => [];

    public Task Launch(
        InvokedTrainLaunch launch,
        IDataContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (launch.InvokedBy.Machine != QueueingTimerMachine.Id)
            return _inner.Launch(launch, context, cancellationToken);
        Interlocked.Increment(ref _throws);
        throw Failure();
    }
}

/// <summary>An effect that counts its runs, shared by every scope so a test reads one count.</summary>
public sealed class SharedCharge : IOrderCharge
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult($"receipt-{Interlocked.Increment(ref _calls)}");
}

/// <summary>A provider error, as a database that dropped the connection raises it.</summary>
internal sealed class DroppedConnectionException()
    : System.Data.Common.DbException("The connection to the database was lost.");
