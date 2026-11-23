using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

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
