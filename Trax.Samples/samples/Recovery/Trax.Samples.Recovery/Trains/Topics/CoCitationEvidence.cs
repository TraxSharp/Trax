using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>
/// What the <c>cocitation</c> branch counted: the pairs that cite at least one work in common, and
/// how many papers have such a partner. The model is asked about it before the count is used.
/// </summary>
public sealed record CoCitationEvidence(
    string RunId,
    int Papers,
    int LinkedPapers,
    IReadOnlyList<PairSignal> Pairs
);
