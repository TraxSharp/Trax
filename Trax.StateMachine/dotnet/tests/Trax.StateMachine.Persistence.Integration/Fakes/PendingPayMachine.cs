using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

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
