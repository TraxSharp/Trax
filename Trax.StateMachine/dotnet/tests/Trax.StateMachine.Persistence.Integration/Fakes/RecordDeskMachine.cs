using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

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
