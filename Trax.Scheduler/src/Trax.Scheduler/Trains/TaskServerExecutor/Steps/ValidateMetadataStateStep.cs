using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectStep;

namespace Trax.Scheduler.Trains.TaskServerExecutor.Steps;

/// <summary>
/// Validates that the Metadata record is in the Pending state before execution.
/// </summary>
internal class ValidateMetadataStateStep(ILogger<ValidateMetadataStateStep> logger)
    : EffectStep<Metadata, Unit>
{
    public override async Task<Unit> Run(Metadata input)
    {
        if (input.TrainState != TrainState.Pending)
        {
            logger.LogWarning(
                "Cannot execute Metadata {MetadataId} with state {State}, must be Pending",
                input.Id,
                input.TrainState
            );
            throw new TrainException(
                $"Cannot execute a job with state {input.TrainState}, must be Pending"
            );
        }

        logger.LogDebug("Metadata {MetadataId} validation passed - state is Pending", input.Id);

        return Unit.Default;
    }
}
