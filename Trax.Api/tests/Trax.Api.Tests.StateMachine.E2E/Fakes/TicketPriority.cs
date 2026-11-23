using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>A priority whose member names are not upper snake case, so a spelling that differs from them shows.</summary>
public enum TicketPriority
{
    Low,
    HighPriority,
}
