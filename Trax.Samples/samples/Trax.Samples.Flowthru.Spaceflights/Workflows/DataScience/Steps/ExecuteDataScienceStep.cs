using Trax.Core.Step;
using Flowthru.Services;
using Flowthru.Services.Models;
using LanguageExt;
using Microsoft.Extensions.Logging;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.DataScience.Steps;

/// <summary>
/// Executes the flowthru DataScience pipeline.
/// Splits data, trains a linear regression model, and evaluates predictions.
///
/// Pipeline logic by @Spelkington — https://github.com/chaoticgoodcomputing/flowthru
/// </summary>
public class ExecuteDataScienceStep(
    IFlowthruService flowthruService,
    ILogger<ExecuteDataScienceStep> logger
) : Step<DataSciencePipelineInput, Unit>
{
    public override async Task<Unit> Run(DataSciencePipelineInput input)
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
