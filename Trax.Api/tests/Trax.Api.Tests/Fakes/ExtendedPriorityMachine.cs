using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

[TraxMachine]
public sealed class ExtendedPriorityMachine()
    : HoldingMachine<ExtendedPriority>("extended-priority")
{
    public const string MachineId = "extended-priority";
}
