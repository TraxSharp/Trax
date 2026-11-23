using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

[TraxMachine]
public sealed class HostBoundMachine() : HoldingMachine<HostBound>("host-bound")
{
    public const string MachineId = "host-bound";
}
