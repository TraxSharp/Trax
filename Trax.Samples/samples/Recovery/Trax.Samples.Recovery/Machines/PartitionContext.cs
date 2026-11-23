using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Samples.Recovery.Trains.Ingest;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>Which partition the instance is for. Every state carries it.</summary>
public sealed record PartitionContext
{
    [MinLength(1)]
    public string Source { get; init; } = "";

    [MinLength(1)]
    public string Month { get; init; } = "";
}
