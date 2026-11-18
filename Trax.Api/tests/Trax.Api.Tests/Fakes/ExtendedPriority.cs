using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>An enum a machine holds and a host's own type extension returns.</summary>
public enum ExtendedPriority
{
    Low,
    VeryHigh,
}
