using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>Enums a record holds only inside framework generics.</summary>
public enum PairedPriority
{
    Low,
}
