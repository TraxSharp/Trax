using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

[TraxMachine]
public sealed class ListedPriorityMachine() : HoldingMachine<ListedPriorities>("listed-priority")
{
    public const string MachineId = "listed-priority";
}
