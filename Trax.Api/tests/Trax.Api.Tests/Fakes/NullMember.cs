using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A member C# spells <c>@null</c>, which the server stores as <see langword="null"/>, a name GraphQL reserves.</summary>
public enum NullMember
{
    Open,
    @null,
}
