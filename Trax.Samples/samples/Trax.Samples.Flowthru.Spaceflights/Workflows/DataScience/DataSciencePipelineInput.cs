using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.DataScience;

public record DataSciencePipelineInput : IManifestProperties
{
    public string PipelineName { get; init; } = "DataScience";
}
