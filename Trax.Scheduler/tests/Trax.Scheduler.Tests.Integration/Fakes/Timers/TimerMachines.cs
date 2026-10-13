using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

public enum ReminderState
{
    Idle,
    Review,
    Expired,
    Done,
    Published,
}

public enum ReminderTrigger
{
    Open,
    Approve,
    Extend,
    Publish,
}

/// <summary>Which definition of <see cref="ReminderMachine"/> a host runs, for the tests of a deploy.</summary>
public enum ReminderShape
{
    /// <summary>Version 1: <c>Review</c> expires after <see cref="ReminderMachine.Delay"/>.</summary>
    V1,

    /// <summary>Version 2, migrated from 1, whose <c>Review</c> has no timer.</summary>
    V2WithoutTimer,

    /// <summary>Version 2, migrated from 1, whose <c>Review</c> expires after <see cref="ReminderMachine.LongerDelay"/>.</summary>
    V2LongerTimer,

    /// <summary>Version 2, migrated from 1, with the same timer as version 1.</summary>
    V2SameTimer,
}

/// <summary>
/// A user's reminder: <c>Idle</c> --Open--> <c>Review</c>, which expires into <c>Expired</c> after
/// <see cref="Delay"/>. From <c>Review</c>, <c>Approve</c> enters <c>Done</c>, <c>Extend</c> enters <c>Review</c>
/// again (restarting its timer) and <c>Publish</c> runs the machine's one effect, <see cref="IReminderPublish"/>,
/// into <c>Published</c>. <c>Expired</c> holds no context marked <c>blocked</c>, so a test can make a fire
/// unappliable by marking it.
/// </summary>
/// <remarks>
/// The assembly scan registers the parameterless version 1 on every host; a test deploys another
/// <see cref="ReminderShape"/> by replacing that registration (see <see cref="TimerHosts.Deploy"/>).
/// </remarks>
public sealed class ReminderMachine : Machine<ReminderState, ReminderTrigger>
{
    public const string MachineId = "timer-reminder";

    public const string EffectKey = "timer-reminder:publish";

    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan LongerDelay = TimeSpan.FromHours(1);

    private readonly ReminderShape _shape;

    public ReminderMachine()
        : this(ReminderShape.V1) { }

    public ReminderMachine(ReminderShape shape) => _shape = shape;

    public ReminderShape Shape => _shape;

    protected override void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m)
    {
        m.Id(MachineId)
            .Version(_shape == ReminderShape.V1 ? 1 : 2)
            .StartsAt(ReminderState.Idle, () => Context());
        if (_shape != ReminderShape.V1)
            m.MigrateFrom(1, (state, context) => new MigrationResult(state, context));

        m.Discard(r => r.OwnerOnly());

        // Open is ungated: a gated event's target is the record of the server's decision, kept past the draft
        // lifetime and never discarded, and these tests bound, discard and re-create reviews.
        m.In(ReminderState.Idle)
            .On(ReminderTrigger.Open)
            .AnyAuthenticated()
            .To(ReminderState.Review);

        var review = m.In(ReminderState.Review);
        review = _shape switch
        {
            ReminderShape.V2WithoutTimer => review,
            ReminderShape.V2LongerTimer => review.After(LongerDelay).GoTo(ReminderState.Expired),
            _ => review.After(Delay).GoTo(ReminderState.Expired),
        };
        review
            .On(ReminderTrigger.Approve)
            .AnyAuthenticated()
            .To(ReminderState.Done)
            .On(ReminderTrigger.Extend)
            .AnyAuthenticated()
            .To(ReminderState.Review)
            .On(ReminderTrigger.Publish)
            .AnyAuthenticated()
            .RunsOnce<IReminderPublish>(EffectKey)
            .To(ReminderState.Published);

        m.In(ReminderState.Expired)
            .Holds(ctx => ctx["blocked"] is null ? null : "An expired reminder is never blocked.");
    }

    /// <summary>The context a reminder starts with.</summary>
    public static JsonObject Context(string note = "") => new() { ["note"] = note };

    /// <summary>A snapshot of the reminder in <paramref name="state"/> as a client sends it.</summary>
    public static string Json(string state, JsonObject? context = null, int version = 1) =>
        new JsonObject
        {
            ["machine"] = MachineId,
            ["version"] = version,
            ["state"] = state,
            ["context"] = context ?? Context(),
        }.ToJsonString();
}

/// <summary>The reminder's one irreversible effect.</summary>
public interface IReminderPublish : ISnapshotEffect;

/// <summary>
/// Publishes at once with a fresh receipt, or, while <see cref="ReminderPublishGate"/> is armed, holds the effect
/// (and so its claim) until the test releases it.
/// </summary>
public sealed class ReminderPublish : IReminderPublish
{
    public async Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (ReminderPublishGate.Armed)
        {
            ReminderPublishGate.Enter();
            await ReminderPublishGate.Released;
        }

        return "published-" + Guid.NewGuid().ToString("N");
    }
}

/// <summary>A test's hold on <see cref="ReminderPublish"/>.</summary>
public static class ReminderPublishGate
{
    private static TaskCompletionSource _entered = New();
    private static TaskCompletionSource _release = New();

    public static bool Armed { get; set; }

    public static Task Entered => _entered.Task;

    public static Task Released => _release.Task;

    public static void Release() => _release.TrySetResult();

    public static void Reset()
    {
        Armed = false;
        _release.TrySetResult();
        _entered = New();
        _release = New();
    }

    internal static void Enter() => _entered.TrySetResult();

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public enum WaitState
{
    Fetching,
    Chained,
    Fetched,
    TimedOut,
    Failed,
    Cancelled,
}

public enum WaitTrigger
{
    Retry,
}

/// <summary>The context of a <see cref="WaitMachine"/>: the run's mode and note, and how long to wait for it.</summary>
public sealed record WaitContext
{
    public string Mode { get; init; } = "";

    public string Note { get; init; } = "";

    public string Artifact { get; init; } = "";

    public TimeSpan? Patience { get; init; }
}

/// <summary>
/// A system-owned wait: it starts in <c>Fetching</c>, which invokes <see cref="IInvokedStepTrain"/> and gives up
/// into <c>TimedOut</c> once it has waited the context's <c>patience</c> (between one minute and one hour). The
/// run's outcome enters <c>Fetched</c>, <c>Failed</c> or <c>Cancelled</c>; <c>Retry</c> enters <c>Fetching</c>
/// again.
/// </summary>
public sealed class WaitMachine : Machine<WaitState, WaitTrigger>
{
    public const string MachineId = "timer-wait";

    public static readonly TimeSpan MinPatience = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan MaxPatience = TimeSpan.FromHours(1);

    protected override void Configure(IMachineBuilder<WaitState, WaitTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .StartsAt(WaitState.Fetching, () => Context(InvokedStepModes.Ok))
            .SystemOwned();

        m.In(WaitState.Fetching)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(ctx["mode"]!.GetValue<string>(), ctx["note"]!.GetValue<string>())
            )
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.Failed)
            .OnCancelled(WaitState.Cancelled)
            .After<WaitContext>(c => c.Patience, MinPatience, MaxPatience)
            .GoTo(WaitState.TimedOut);

        m.In(WaitState.TimedOut).On(WaitTrigger.Retry).To(WaitState.Fetching);
        m.In(WaitState.Failed).On(WaitTrigger.Retry).To(WaitState.Fetching);
    }

    /// <summary>A context for a run in <paramref name="mode"/>, waiting <paramref name="patience"/> (ISO 8601).</summary>
    public static JsonObject Context(string mode, string patience = "PT5M", string? note = null) =>
        new()
        {
            ["mode"] = mode,
            ["note"] = note ?? Guid.NewGuid().ToString("N"),
            ["artifact"] = "",
            ["patience"] = patience,
        };
}

/// <summary>
/// A system-owned wait whose run's failure strands it: <c>Fetching</c>'s <c>OnFailed</c> enters <c>Chained</c>,
/// whose run's input reads a field no context has, so that run cannot be queued and the instance stays in
/// <c>Fetching</c> with no live run. <c>Fetching</c> gives up into <c>TimedOut</c> after <see cref="Delay"/>.
/// </summary>
public sealed class StrandingWaitMachine : Machine<WaitState, WaitTrigger>
{
    public const string MachineId = "timer-wait-strand";

    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    protected override void Configure(IMachineBuilder<WaitState, WaitTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .StartsAt(WaitState.Fetching, () => WaitMachine.Context(InvokedStepModes.Ok))
            .SystemOwned();

        m.In(WaitState.Fetching)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(ctx["mode"]!.GetValue<string>(), ctx["note"]!.GetValue<string>())
            )
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.Chained)
            .OnCancelled(WaitState.Cancelled)
            .After(Delay)
            .GoTo(WaitState.TimedOut);

        m.In(WaitState.Chained)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(ctx["missing"]!.GetValue<string>(), "")
            )
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.Failed)
            .OnCancelled(WaitState.Cancelled);

        m.In(WaitState.Failed).On(WaitTrigger.Retry).To(WaitState.Fetching);
    }
}
