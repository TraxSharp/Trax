using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A member starting with two underscores, which GraphQL reserves for introspection.</summary>
public enum DunderMember
{
    Open,
    __Hidden,
}
