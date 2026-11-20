using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>What the join wrote, and every pair it weighed.</summary>
public sealed record CombinedSignals(
    string RunId,
    int Papers,
    string CoCitationTrack,
    int Written,
    IReadOnlyList<TopicLink> Links
);
