using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Tests.Fakes;

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
/// A user-owned machine with a timer: <c>Review</c> is held for ten minutes, then the server moves it to
/// <c>Expired</c>. <c>Extend</c> re-enters <c>Review</c>, which starts the timer again. The CLI's artifacts and
/// drawings must carry the timer edge (its trigger, <c>Review.after</c>, and its duration), which a client
/// can never send. Declared as the Trax.Effect tests' ReminderMachine is, whose exported IR is the committed
/// reminder.ir.json.
/// </summary>
public sealed class DeclarativeReminderMachine : Machine<ReminderState, ReminderTrigger>
{
    protected override void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m)
    {
        m.Id("reminder").Version(1).StartsAt(ReminderState.Idle, () => new JsonObject());

        m.In(ReminderState.Idle)
            .Context()
            .On(ReminderTrigger.Open)
            .Requires("Editor")
            .To(ReminderState.Review);

        m.In(ReminderState.Review)
            .Context()
            .After(TimeSpan.FromMinutes(10))
            .GoTo(ReminderState.Expired)
            .On(ReminderTrigger.Approve)
            .AnyAuthenticated()
            .To(ReminderState.Done)
            .On(ReminderTrigger.Extend)
            .AnyAuthenticated()
            .To(ReminderState.Review);

        m.In(ReminderState.Expired).Context();
        m.In(ReminderState.Done).Context();
    }
}
