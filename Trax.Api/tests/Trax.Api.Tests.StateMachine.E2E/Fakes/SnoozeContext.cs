using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

public sealed record SnoozeContext
{
    /// <summary>How long <see cref="SnoozeState.Waiting"/> is held before it lapses.</summary>
    public TimeSpan? Patience { get; init; }
}
