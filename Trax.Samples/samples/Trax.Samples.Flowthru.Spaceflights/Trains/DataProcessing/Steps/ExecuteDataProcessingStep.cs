using Flowthru.Services;
using Flowthru.Services.Models;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.DataProcessing.Steps;

/// <summary>
/// Executes the flowthru DataProcessing pipeline.
/// Preprocesses companies.csv, reviews.csv, and shuttles.xlsx into a ModelInputTable.
///
/// Pipeline logic by @Spelkington — https://github.com/chaoticgoodcomputing/flowthru
/// </summary>
public class ExecuteDataProcessingStep(
    IFlowthruService flowthruService,
    ILogger<ExecuteDataProcessingStep> logger
) : Step<DataProcessingPipelineInput, Unit>
{
    public override async Task<Unit> Run(DataProcessingPipelineInput input)
    {
        logger.LogInformation("Executing flowthru pipeline: {PipelineName}", input.PipelineName);

        var request = new PipelineExecutionRequest
        {
            PipelineName = input.PipelineName,
            ExportMetadata = false,
        };

        var result = await flowthruService.ExecutePipelineAsync(request, CancellationToken);

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
