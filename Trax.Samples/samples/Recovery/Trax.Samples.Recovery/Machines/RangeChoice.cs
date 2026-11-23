using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>The input of <see cref="TopicMapTrigger.Build"/>.</summary>
public sealed record RangeChoice
{
    public int FromYear { get; init; }

    public int ToYear { get; init; }
}
