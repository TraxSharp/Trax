using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

public sealed record TicketContext
{
    public string? Title { get; init; }

    public TicketPriority? Priority { get; init; }
}
