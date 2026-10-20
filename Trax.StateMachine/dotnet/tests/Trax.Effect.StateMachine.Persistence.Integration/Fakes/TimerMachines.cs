using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

public enum ReminderState
{
    Idle,
    Review,
    Expired,
    Done,
    Archived,
}

public enum ReminderTrigger
{
    Open,
    Approve,
    Extend,
}

/// <summary>
/// A user's reminder with a timed state: an editor opens it into <c>Review</c>, which expires after
/// <see cref="ReviewFor"/> unless it is approved first or extended (a self-entry that restarts the timer).
/// <c>Expired</c> is timed too, so a timer firing lands in another timed state.
/// </summary>
public sealed class ReminderMachine : Machine<ReminderState, ReminderTrigger>
{
    public const string Id = "reminder";

    /// <summary>The name of the timer trigger of <c>Review</c>.</summary>
    public const string ReviewTimer = "Review.after";

    public static readonly TimeSpan ReviewFor = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan ExpiredFor = TimeSpan.FromDays(1);

    public sealed record ApproveInput
    {
        public string Verdict { get; init; } = "";
    }

    protected override void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m) =>
        Declare(m, Id, gated: true);

    /// <summary>The reminder under <paramref name="id"/>; <paramref name="gated"/> puts a role rule on <c>Open</c>.</summary>
    internal static void Declare(
        IMachineBuilder<ReminderState, ReminderTrigger> m,
        string id,
        bool gated
    )
    {
        m.Id(id).Version(1).StartsAt(ReminderState.Idle, () => new JsonObject());

        var open = m.In(ReminderState.Idle).Context().On(ReminderTrigger.Open);
        (gated ? open.Requires("Editor") : open).To(ReminderState.Review);

        // Once one event declares a rule, every event a client sends declares one.
        var approve = m.In(ReminderState.Review)
            .Context()
            .After(ReviewFor)
            .GoTo(ReminderState.Expired)
            .On(ReminderTrigger.Approve)
            .WithInput<ApproveInput>()
            .When(Input((ApproveInput i) => i.Verdict).IsOneOf("approved"))
            .Because("Only an approved verdict closes the review.");
        var extend = (gated ? approve.AnyAuthenticated() : approve)
            .To(ReminderState.Done)
            .On(ReminderTrigger.Extend);
        (gated ? extend.AnyAuthenticated() : extend).To(ReminderState.Review);

        m.In(ReminderState.Expired).Context().After(ExpiredFor).GoTo(ReminderState.Archived);
        m.In(ReminderState.Done).Context();
        m.In(ReminderState.Archived).Context();
    }

    /// <summary>A snapshot of the machine under <paramref name="machine"/> as a client sends it.</summary>
    public static string Json(string state, string machine = Id) =>
        new JsonObject
        {
            ["machine"] = machine,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = new JsonObject(),
        }.ToJsonString();
}

/// <summary>The reminder as a system-owned machine, with no rule on <c>Open</c>.</summary>
public sealed class SystemReminderMachine : Machine<ReminderState, ReminderTrigger>
{
    public const string Id = "system-reminder";

    protected override void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m)
    {
        ReminderMachine.Declare(m, Id, gated: false);
        m.SystemOwned();
    }
}

public enum PlainTimerState
{
    Idle,
    Waiting,
}

public enum PlainTimerTrigger
{
    Wait,
    Stop,
}

/// <summary>
/// A machine whose only server-written feature is a timer: no rule, no invoke, no effect. The generic save is
/// refused for it because of the timer alone.
/// </summary>
public sealed class PlainTimerMachine : Machine<PlainTimerState, PlainTimerTrigger>
{
    public const string Id = "plain-timer";

    protected override void Configure(IMachineBuilder<PlainTimerState, PlainTimerTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(PlainTimerState.Idle, () => new JsonObject());

        m.In(PlainTimerState.Idle).Context().On(PlainTimerTrigger.Wait).To(PlainTimerState.Waiting);

        m.In(PlainTimerState.Waiting)
            .Context()
            .After(TimeSpan.FromMinutes(5))
            .GoTo(PlainTimerState.Idle)
            .On(PlainTimerTrigger.Stop)
            .To(PlainTimerState.Idle);
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

public enum RenewalState
{
    Draft,
    Active,
    Lapsed,
}

public enum RenewalTrigger
{
    Charge,
}

/// <summary>
/// A user's subscription whose irreversible charge lands in the timed <c>Active</c>, which lapses after
/// <see cref="ActiveFor"/>: the effect runner's write enters a timed state.
/// </summary>
public sealed class RenewalMachine : Machine<RenewalState, RenewalTrigger>
{
    public const string Id = "renewal";

    public static readonly TimeSpan ActiveFor = TimeSpan.FromDays(30);

    protected override void Configure(IMachineBuilder<RenewalState, RenewalTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(RenewalState.Draft, () => new JsonObject());

        m.In(RenewalState.Draft)
            .On(RenewalTrigger.Charge)
            .RunsOnce<IOrderCharge>()
            .Reduce((_, input) => new JsonObject { ["receipt"] = input?["receipt"]?.DeepClone() })
            .To(RenewalState.Active);

        m.In(RenewalState.Active).After(ActiveFor).GoTo(RenewalState.Lapsed);
    }

    public static string DraftJson =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = "Draft",
            ["context"] = new JsonObject(),
        }.ToJsonString();
}

public enum ExpiringState
{
    Open,
    Closed,
}

public enum ExpiringTrigger
{
    Close,
}

/// <summary>What a client starts an <see cref="ExpiringMachine"/> with: nothing.</summary>
public sealed record ExpiringStart;

/// <summary>A user's machine whose initial state is timed: a started instance closes itself after an hour.</summary>
public sealed class ExpiringMachine : Machine<ExpiringState, ExpiringTrigger>
{
    public const string Id = "expiring";

    public static readonly TimeSpan OpenFor = TimeSpan.FromHours(1);

    protected override void Configure(IMachineBuilder<ExpiringState, ExpiringTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ExpiringState.Open, () => new JsonObject())
            .Start<ExpiringStart>(s => s.AnyAuthenticated());

        m.In(ExpiringState.Open)
            .Context()
            .After(OpenFor)
            .GoTo(ExpiringState.Closed)
            .On(ExpiringTrigger.Close)
            .AnyAuthenticated()
            .To(ExpiringState.Closed);

        m.In(ExpiringState.Closed).Context();
    }
}

public sealed record WaitInput(string Source);

public sealed record WaitOutput
{
    public string Body { get; init; } = "";
}

/// <summary>The train <see cref="WaitMachine"/> invokes. These tests never run it.</summary>
public interface IWaitTrain : IServiceTrain<WaitInput, WaitOutput>;

public enum WaitState
{
    Idle,
    Fetching,
    Fetched,
    Cooldown,
    Stopped,
    TimedOut,
}

public enum WaitTrigger
{
    Fetch,
    Abandon,
}

/// <summary>
/// A user's machine with a timed invoking state: <c>Fetching</c> invokes <see cref="IWaitTrain"/> and leaves for
/// <c>TimedOut</c> once it has been held for the context's <c>Patience</c> (between one minute and one hour). A
/// failed run lands in the timed <c>Cooldown</c>; a cancelled one in the untimed <c>Stopped</c>.
/// </summary>
public sealed class WaitMachine : Machine<WaitState, WaitTrigger>
{
    public const string Id = "wait";

    public static readonly TimeSpan CooldownFor = TimeSpan.FromMinutes(5);

    public sealed record WaitContext
    {
        public string Source { get; init; } = "";

        public TimeSpan? Patience { get; init; }
    }

    protected override void Configure(IMachineBuilder<WaitState, WaitTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(WaitState.Idle, () => Context(TimeSpan.FromMinutes(5)));

        m.In(WaitState.Idle).Context<WaitContext>().On(WaitTrigger.Fetch).To(WaitState.Fetching);

        m.In(WaitState.Fetching)
            .Context<WaitContext>()
            .Invokes<IWaitTrain, WaitInput, WaitOutput>(ctx => new WaitInput(
                ctx["source"]!.GetValue<string>()
            ))
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.Cooldown)
            .OnCancelled(WaitState.Stopped)
            .After<WaitContext>(c => c.Patience, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1))
            .GoTo(WaitState.TimedOut)
            .On(WaitTrigger.Abandon)
            .To(WaitState.Idle);

        m.In(WaitState.Cooldown).Context<WaitContext>().After(CooldownFor).GoTo(WaitState.Idle);
        m.In(WaitState.Fetched).Context<WaitContext>();
        m.In(WaitState.Stopped).Context<WaitContext>();
        m.In(WaitState.TimedOut).Context<WaitContext>();
    }

    /// <summary>The context with <paramref name="patience"/> as its duration, written as ISO-8601.</summary>
    public static JsonObject Context(TimeSpan patience) =>
        new() { ["source"] = "repo", ["patience"] = System.Xml.XmlConvert.ToString(patience) };

    public static string Json(string state, TimeSpan patience) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = Context(patience),
        }.ToJsonString();
}
