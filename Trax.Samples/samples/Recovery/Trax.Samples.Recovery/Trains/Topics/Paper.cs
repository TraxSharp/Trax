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
