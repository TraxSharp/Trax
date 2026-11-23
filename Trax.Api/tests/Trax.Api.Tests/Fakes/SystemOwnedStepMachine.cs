using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A declarative machine whose instances belong to the system: no client may reach one.</summary>
[TraxMachine]
public sealed class SystemOwnedStepMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "system-owned-step";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject()).SystemOwned();
        m.In(StepState.First).Context().On(StepTrigger.Next).To(StepState.Second);
        m.In(StepState.Second).Context().On(StepTrigger.Back).To(StepState.First);
    }
}
