using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>
/// The manifest's input: which run, and which slice of the corpus to map. Like the other scenarios'
/// inputs, it holds only what the page chose at the start, so a retry's input is byte-identical.
/// </summary>
public record TopicMapInput : IManifestProperties
{
    public required string RunId { get; init; }

    /// <summary>The fields to map, from <c>CorpusFixture.Fields</c>.</summary>
    public required IReadOnlyList<string> Fields { get; init; }

    public required int FromYear { get; init; }

    public required int ToYear { get; init; }
}
