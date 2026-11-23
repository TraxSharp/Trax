using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A machine holding Trax's own <see cref="Trax.Effect.Enums.TrainState"/>, which Trax's subscription carries.</summary>
[TraxMachine]
public sealed class TrainStateHoldingMachine()
    : HoldingMachine<Trax.Effect.Enums.TrainState>("train-state-holding")
{
    public const string MachineId = "train-state-holding";
}
