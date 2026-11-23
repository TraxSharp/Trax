using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A machine whose push field is <c>orderChanged</c>.</summary>
[TraxMachine]
public sealed class OrderStepMachine() : NamedStepMachine("order")
{
    public const string MachineId = "order";
}
