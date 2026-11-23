using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>An enum a machine and a GraphQL train both reach.</summary>
public enum SharedPriority
{
    Low,
    VeryHigh,
}
