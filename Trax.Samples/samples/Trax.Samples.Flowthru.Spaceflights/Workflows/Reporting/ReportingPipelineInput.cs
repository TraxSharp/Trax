using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.Reporting;

public record ReportingPipelineInput : IManifestProperties
{
    public string PipelineName { get; init; } = "Reporting";
}
