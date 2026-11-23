using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

public sealed record ShopContext
{
    public string? Item { get; init; }

    public int? Quantity { get; init; }

    public string? Street { get; init; }
}
