using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

[TraxMachine]
public sealed class LedgerMachine : Machine<LedgerStage, LedgerTrigger>
{
    public const string MachineId = "ledger";

    protected override void Configure(IMachineBuilder<LedgerStage, LedgerTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(LedgerStage.Drafted, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(LedgerStage.Drafted)
            .Context()
            .On(LedgerTrigger.Post)
            .AnyAuthenticated()
            .To(LedgerStage.Posted);
    }
}
