using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>What a client starts a snooze with, and snoozes again with.</summary>
public sealed record SnoozeInput
{
    public TimeSpan Patience { get; init; }
}
