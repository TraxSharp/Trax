using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>
/// The train's output. <see cref="RunId"/> is the key the join wrote the pairs under, so a state machine
/// that ran the build keeps it as a pointer to them.
/// </summary>
public sealed record TopicMap(
    string RunId,
    int Papers,
    int TopicPairs,
    string CoCitationTrack,
    IReadOnlyList<TopicLink> Strongest,
    IReadOnlyList<TopicLink> HiddenTwins
);
