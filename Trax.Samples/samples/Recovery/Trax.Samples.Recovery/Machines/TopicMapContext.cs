using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>
/// The wizard's draft, one shape in every state: what the user chose, and once a build finished, a pointer
/// to what it wrote and a summary of it. Never the pairs themselves: those are in
/// <c>topic_map.topic_pairs</c> under <see cref="MapId"/>.
/// </summary>
public sealed record TopicMapContext
{
    public List<string>? Fields { get; init; }

    public int? FromYear { get; init; }

    public int? ToYear { get; init; }

    /// <summary>The run id the build wrote its pairs under.</summary>
    public string? MapId { get; init; }

    public int? Papers { get; init; }

    public int? TopicPairs { get; init; }

    public string? CoCitationTrack { get; init; }
}
