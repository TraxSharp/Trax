using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

public sealed record SharedPriorityInput
{
    public SharedPriority Priority { get; init; }
}
