using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainBus;
using Trax.Scheduler.Services.DormantDependentContext;

namespace Trax.Scheduler.Trains.JobRunner.Junctions;

/// <summary>
/// Executes the target train using the TrainBus with the resolved input.
/// </summary>
internal class RunScheduledTrainJunction(
    ITrainBus trainBus,
    DormantDependentContext dormantDependentContext,
    ILogger<RunScheduledTrainJunction> logger
) : EffectJunction<(Metadata, ResolvedTrainInput), Unit>
{
    public override async Task<Unit> Run((Metadata, ResolvedTrainInput) input)
    {
        var (metadata, resolvedInput) = input;

        // Initialize the dormant dependent context so user train steps
        // can activate dormant dependents of this parent manifest
        if (metadata.ManifestId.HasValue)
            dormantDependentContext.Initialize(metadata.ManifestId.Value);

        logger.LogDebug(
            "Executing train {TrainName} for Metadata {MetadataId}",
            metadata.Name,
            metadata.Id
        );

        await trainBus.RunAsync(resolvedInput.Value, CancellationToken, metadata);

        logger.LogDebug(
            "Successfully executed train {TrainName} for Metadata {MetadataId}",
            metadata.Name,
            metadata.Id
        );

        return Unit.Default;
    }
}
