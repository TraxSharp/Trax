using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>An enum a machine holds only through a class deriving from a list.</summary>
public enum ListedPriority
{
    Low,
    VeryHigh,
}
