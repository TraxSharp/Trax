using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>The input of <see cref="ShelfTrigger.File"/> in <see cref="ShelfShape.RecursiveInput"/>.</summary>
public sealed record ShelfFileInput
{
    public ShelfNode? Node { get; init; }
}
