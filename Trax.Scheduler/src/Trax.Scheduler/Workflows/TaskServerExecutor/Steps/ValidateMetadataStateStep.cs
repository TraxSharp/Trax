using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectStep;
using Trax.Core.Exceptions;
using LanguageExt;
using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Workflows.TaskServerExecutor.Steps;

/// <summary>
/// Validates that the Metadata record is in the Pending state before execution.
/// </summary>
internal class ValidateMetadataStateStep(ILogger<ValidateMetadataStateStep> logger)
    : EffectStep<Metadata, Unit>
{
    public override async Task<Unit> Run(Metadata input)
    {
        if (input.WorkflowState != WorkflowState.Pending)
        {
            logger.LogWarning(
                "Cannot execute Metadata {MetadataId} with state {State}, must be Pending",
                input.Id,
                input.WorkflowState
            );
            throw new WorkflowException(
                $"Cannot execute a job with state {input.WorkflowState}, must be Pending"
            );
        }

        logger.LogDebug("Metadata {MetadataId} validation passed - state is Pending", input.Id);

        return Unit.Default;
    }
}
