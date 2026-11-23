using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>The context of <see cref="ShelfShape.RecursiveContext"/>, which holds a recursive record.</summary>
public sealed record ShelfTreeContext
{
    public string? Label { get; init; }

    public ShelfNode? Node { get; init; }
}
