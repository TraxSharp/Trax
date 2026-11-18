using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>
/// Declares rules, but one event's guard is a delegate: the IR and its hash do not cover the lambda, so changing it
/// would never make a client outdated.
/// </summary>
[TraxMachine]
public sealed class MixedGuardMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "mixed-guard";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First)
            .Context()
            .On(StepTrigger.Next)
            .AnyAuthenticated()
            .When((ctx, _) => ctx.Count == 0)
            .Because("Only an empty context moves on.")
            .To(StepState.Second);
        m.In(StepState.Second)
            .Context()
            .On(StepTrigger.Back)
            .AnyAuthenticated()
            .To(StepState.First);
    }
}
