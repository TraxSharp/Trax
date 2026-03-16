using Flowthru.Pipelines;
using Flowthru.Services;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.Reporting.Junctions;

/// <summary>
/// Executes the flowthru Reporting pipeline.
/// Generates passenger capacity reports, charts, and PNG exports.
///
/// Pipeline logic by @Spelkington — https://github.com/chaoticgoodcomputing/flowthru
/// </summary>
public class ExecuteReportingJunction(
    IFlowthruService flowthruService,
    ILogger<ExecuteReportingJunction> logger
) : Junction<ReportingPipelineInput, Unit>
{
    public override async Task<Unit> Run(ReportingPipelineInput input)
    {
        logger.LogInformation("Executing flowthru pipeline: {PipelineName}", input.PipelineName);

        var options = new ExecutionOptions
        {
            SliceStrategy = new PipelineSliceStrategy
            {
                Pipelines = new System.Collections.Generic.HashSet<string> { input.PipelineName },
            },
        };

        var result = await flowthruService.ExecutePipelineAsync(
            options,
            exportMetadata: false,
            cancellationToken: CancellationToken
        );

        if (!result.Success)
        {
            throw result.Exception
                ?? new InvalidOperationException(
                    $"Pipeline '{input.PipelineName}' failed without an exception."
                );
        }

        logger.LogInformation(
            "Pipeline '{PipelineName}' completed in {Duration:F2}s ({NodeCount} nodes)",
            input.PipelineName,
            result.ExecutionTime.TotalSeconds,
            result.NodeResults.Count
        );

        return Unit.Default;
    }
}
