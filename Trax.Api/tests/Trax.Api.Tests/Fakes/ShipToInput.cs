using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>The record two machines' events share, which the schema registers once.</summary>
public sealed record ShipToInput
{
    public string Street { get; init; } = "";
}
