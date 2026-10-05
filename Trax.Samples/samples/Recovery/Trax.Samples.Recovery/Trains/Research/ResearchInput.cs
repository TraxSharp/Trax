using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Research;

/// <summary>
/// The manifest's input. It must stay byte-identical between attempts for a retry to replay the
/// first attempt's decisions, so it holds only what the page chose at the start.
/// </summary>
public record ResearchInput : IManifestProperties
{
    public required string RunId { get; init; }
    public required string Topic { get; init; }
}
