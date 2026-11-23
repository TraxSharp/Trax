using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>An enum the host binds a GraphQL type to itself.</summary>
public enum HostBound
{
    Low,
    VeryHigh,
}
