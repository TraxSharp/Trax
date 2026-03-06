using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.DataProcessing;

public record DataProcessingPipelineInput : IManifestProperties
{
    public string PipelineName { get; init; } = "DataProcessing";
}
