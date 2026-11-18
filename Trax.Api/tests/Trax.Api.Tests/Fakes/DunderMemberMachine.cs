using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

[TraxMachine]
public sealed class DunderMemberMachine() : HoldingMachine<DunderMember>("dunder-member")
{
    public const string MachineId = "dunder-member";
}
