using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Systematic.Orders;

[TraxMachine]
public sealed class GradedMachine() : Trax.Api.Tests.Fakes.Machines.HoldingMachine<Graded>("graded")
{
    public const string MachineId = "graded";
}
