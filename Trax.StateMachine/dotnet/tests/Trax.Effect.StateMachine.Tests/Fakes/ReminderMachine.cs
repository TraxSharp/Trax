using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Tests.Fakes;

public enum ReminderState
{
    Idle,
    Review,
    Expired,
    Done,
}

public enum ReminderTrigger
{
    Open,
    Approve,
    Extend,
}

/// <summary>
/// A user-owned machine with one timer: <c>Idle -(Open, Editor)-> Review</c>, <c>Review -(Approve)-> Done</c>,
/// <c>Review -(Extend, any authenticated)-> Review</c>, and Review's timer moves it to Expired after ten minutes.
/// Its exported IR is the committed <c>machines/reminder/reminder.ir.json</c> the TypeScript twin is generated from,
/// which <c>TimerDeclarationTests</c> compares with it.
/// </summary>
public static class ReminderMachine
{
    public static readonly TimeSpan ReviewWindow = TimeSpan.FromMinutes(10);

    public static readonly BuiltMachine<ReminderState, ReminderTrigger> Built = Build();
    public static SnapshotMachine<ReminderState, ReminderTrigger> Machine => Built.Engine;

    /// <summary>The same declaration, read through <see cref="Persistence.IMachine"/>.</summary>
    public static ConfiguredMachine<ReminderState, ReminderTrigger> AsMachine() => new(Configure);

    public static void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m)
    {
        m.Id("reminder").Version(1).StartsAt(ReminderState.Idle, () => new JsonObject());

        m.In(ReminderState.Idle)
            .Context()
            .On(ReminderTrigger.Open)
            .Requires("Editor")
            .To(ReminderState.Review);

        m.In(ReminderState.Review)
            .Context()
            .After(ReviewWindow)
            .GoTo(ReminderState.Expired)
            .On(ReminderTrigger.Approve)
            .To(ReminderState.Done)
            .On(ReminderTrigger.Extend)
            .AnyAuthenticated()
            .To(ReminderState.Review);

        m.In(ReminderState.Expired).Context();
        m.In(ReminderState.Done).Context();
    }

    public static Snapshot At(ReminderState state) =>
        new()
        {
            Machine = "reminder",
            Version = 1,
            State = state.ToString(),
            Context = new JsonObject(),
        };

    private static BuiltMachine<ReminderState, ReminderTrigger> Build()
    {
        var m = new MachineBuilder<ReminderState, ReminderTrigger>();
        Configure(m);
        return m.Build();
    }
}
