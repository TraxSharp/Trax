using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

public enum PendingPayState
{
    Draft,
    Review,
    Paid,
    Cancelled,
    Expired,
}

public enum PendingPayTrigger
{
    Go,
    Pay,
    Cancel,
    Edit,
    Restart,
}

public sealed record PendingPayContext
{
    public string Note { get; init; } = "";
    public string Receipt { get; init; } = "";
}

public sealed record PendingNoteInput
{
    public string Note { get; init; } = "";
}

public sealed record PendingReceiptInput
{
    public string Receipt { get; init; } = "";
}

/// <summary>
/// A payment with its effect in a timed state that other events also leave: <c>Draft -Go-&gt; Review</c>; from
/// <c>Review</c>, <c>Pay</c> runs <see cref="IOrderCharge"/> once and records its receipt in <c>Paid</c>,
/// <c>Cancel</c> goes to <c>Cancelled</c>, <c>Edit</c> writes a note and stays, <c>Restart</c> resets to
/// <c>Draft</c>, and the timer moves it to <c>Expired</c> after ten minutes. Every rule is open to the owner.
/// </summary>
public sealed class PendingPayMachine : Machine<PendingPayState, PendingPayTrigger>
{
    public const string Id = "pending-pay";

    protected override void Configure(IMachineBuilder<PendingPayState, PendingPayTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(
                PendingPayState.Draft,
                () => new JsonObject { ["note"] = "", ["receipt"] = "" }
            )
            .Start<DeskStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(PendingPayState.Draft)
            .Context<PendingPayContext>()
            .On(PendingPayTrigger.Go)
            .AnyAuthenticated()
            .To(PendingPayState.Review);

        m.In(PendingPayState.Review)
            .Context<PendingPayContext>()
            .After(TimeSpan.FromMinutes(10))
            .GoTo(PendingPayState.Expired)
            .On(PendingPayTrigger.Pay)
            .AnyAuthenticated()
            .RunsOnce<IOrderCharge>($"{Id}:pay")
            .WithInput<PendingReceiptInput>()
            .Reduce(
                Set((PendingPayContext c) => c.Receipt)
                    .FromInput((PendingReceiptInput i) => i.Receipt)
            )
            .To(PendingPayState.Paid)
            .On(PendingPayTrigger.Cancel)
            .AnyAuthenticated()
            .To(PendingPayState.Cancelled)
            .On(PendingPayTrigger.Edit)
            .AnyAuthenticated()
            .WithInput<PendingNoteInput>()
            .Reduce(Set((PendingPayContext c) => c.Note).FromInput((PendingNoteInput i) => i.Note))
            .To(PendingPayState.Review)
            .On(PendingPayTrigger.Restart)
            .AnyAuthenticated()
            .Reduce(Reset())
            .To(PendingPayState.Draft);

        m.In(PendingPayState.Paid).Context<PendingPayContext>();
        m.In(PendingPayState.Cancelled).Context<PendingPayContext>();
        m.In(PendingPayState.Expired).Context<PendingPayContext>();
    }

    /// <summary>The instance in <paramref name="state"/> with an empty note and receipt.</summary>
    public static Snapshot At(PendingPayState state) =>
        new()
        {
            Machine = Id,
            Version = 1,
            State = state.ToString(),
            Context = new JsonObject { ["note"] = "", ["receipt"] = "" },
        };
}

public enum RecordDeskState
{
    Draft,
    Review,
    Revising,
    Archived,
}

public enum RecordDeskTrigger
{
    Submit,
    Revise,
    Reject,
    Note,
    Resume,
}

/// <summary>
/// A desk whose <c>Revising</c> both its owner and an operator can enter: <c>Draft -Revise-&gt; Revising</c> by the
/// owner, <c>Draft -Submit-&gt; Review -Reject(Operator)-&gt; Revising</c> by an operator. In <c>Revising</c> the owner
/// may add a note (a self-loop) or resume to <c>Draft</c>, and the timer archives it after ten minutes.
/// </summary>
public sealed class RecordDeskMachine : Machine<RecordDeskState, RecordDeskTrigger>
{
    public const string Id = "record-desk";

    protected override void Configure(IMachineBuilder<RecordDeskState, RecordDeskTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(RecordDeskState.Draft, () => new JsonObject())
            .Start<DeskStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(RecordDeskState.Draft)
            .On(RecordDeskTrigger.Submit)
            .AnyAuthenticated()
            .To(RecordDeskState.Review)
            .On(RecordDeskTrigger.Revise)
            .AnyAuthenticated()
            .To(RecordDeskState.Revising);

        m.In(RecordDeskState.Review)
            .On(RecordDeskTrigger.Reject)
            .Requires(DeskRoles.Operator)
            .To(RecordDeskState.Revising);

        m.In(RecordDeskState.Revising)
            .After(TimeSpan.FromMinutes(10))
            .GoTo(RecordDeskState.Archived)
            .On(RecordDeskTrigger.Note)
            .AnyAuthenticated()
            .To(RecordDeskState.Revising)
            .On(RecordDeskTrigger.Resume)
            .AnyAuthenticated()
            .To(RecordDeskState.Draft);

        m.In(RecordDeskState.Archived);
    }
}
