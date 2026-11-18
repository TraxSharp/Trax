using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E.Fakes;

public sealed record BadgeContext
{
    public string? Holder { get; init; }
}
