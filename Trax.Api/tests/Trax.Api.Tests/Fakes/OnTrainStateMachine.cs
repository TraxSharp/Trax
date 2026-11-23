using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A machine whose push field is <c>onTrainStateChanged</c>, which Trax's own subscription already is.</summary>
[TraxMachine]
public sealed class OnTrainStateMachine() : NamedStepMachine("on-train-state")
{
    public const string MachineId = "on-train-state";
}
