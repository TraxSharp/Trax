using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>One paper as the topic map reads it.</summary>
public sealed record Paper(
    string Id,
    string Title,
    string Abstract,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> References,
    IReadOnlyList<string> Concepts
);

/// <summary>The papers the run maps: the fields and years its input chose. Every branch reads it.</summary>
public sealed record CorpusSlice(string RunId, IReadOnlyList<Paper> Papers);

/// <summary>One signal's value for two papers, <see cref="WorkA"/> being the lower id.</summary>
public sealed record PairSignal(string WorkA, string WorkB, double Value);

/// <summary>The <c>embedding</c> branch's result: how alike every two abstracts read, from 0 to 1.</summary>
public sealed record EmbeddingSignal(IReadOnlyList<PairSignal> Pairs);

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

/// <summary>
/// The <c>cocitation</c> branch's result: the shared-reference counts and how much the map should
/// trust them, which the track the model's answer chose decides.
/// </summary>
public sealed record CoCitationSignal(string Track, double Weight, IReadOnlyList<PairSignal> Pairs);

/// <summary>The <c>authors</c> branch's result: the pairs written by at least one author in common.</summary>
public sealed record AuthorSignal(IReadOnlyList<PairSignal> Pairs);

/// <summary>Two papers with every signal the map has for them.</summary>
public sealed record TopicLink(
    string WorkA,
    string TitleA,
    string WorkB,
    string TitleB,
    double Embedding,
    int SharedReferences,
    int SharedAuthors,
    double Score
);

/// <summary>What the join wrote, and every pair it weighed.</summary>
public sealed record CombinedSignals(
    string RunId,
    int Papers,
    string CoCitationTrack,
    int Written,
    IReadOnlyList<TopicLink> Links
);

/// <summary>The train's output.</summary>
public sealed record TopicMap(
    int Papers,
    int TopicPairs,
    string CoCitationTrack,
    IReadOnlyList<TopicLink> Strongest,
    IReadOnlyList<TopicLink> HiddenTwins
);

[Asks(
    "In this slice of papers, do papers that cite the same works tend to be about the same topic?",
    Yes = "Trust it: shared references mark papers on one topic here.",
    No = "Ignore it: shared references here are standard citations every paper carries."
)]
public sealed class SameTopic;
