using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>A node that holds another of its kind: no finite schema describes it.</summary>
public sealed record ShelfNode
{
    public string Name { get; init; } = "";

    public ShelfNode? Child { get; init; }
}
