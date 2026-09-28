using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainBus;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Trains.JobRunner.Junctions;

/// <summary>
/// Executes the target train using the TrainBus with the resolved input, then records the
/// success on its manifest.
/// </summary>
/// <remarks>
/// The manifest update is part of this junction, not a junction of its own, because a train
/// checks its token before every junction. Once the scheduled train has completed, a
/// cancellation (a host shutdown) must not stop the JobRunner recording that it did: a lost
/// update leaves <c>LastSuccessfulRun</c> stale, <c>NextScheduledRun</c> uncomputed and a
/// <c>Once</c> manifest enabled to run again. A following junction would be skipped by exactly
/// that check, so the update and its save run here, on an uncancellable token. See
/// scheduler/0005.
/// </remarks>
internal class RunScheduledTrainJunction(
    ITrainBus trainBus,
    IDataContext dataContext,
    DormantDependentContext dormantDependentContext,
    ILogger<RunScheduledTrainJunction> logger
) : EffectJunction<(Metadata, ResolvedTrainInput), Unit>
{
    public override async Task<Unit> Run((Metadata, ResolvedTrainInput) input)
    {
        var (metadata, resolvedInput) = input;

        // Initialize the dormant dependent context so user train junctions
        // can activate dormant dependents of this parent manifest.
        // Uses AsyncLocal to flow across the DI scope boundary created by TrainBus.RunAsync.
        if (metadata.ManifestId.HasValue)
            dormantDependentContext.Initialize(metadata.ManifestId.Value);

        try
        {
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
        }
        finally
        {
            // Clear the AsyncLocal to prevent stale manifest IDs from leaking
            // into subsequent job executions on the same worker task.
            dormantDependentContext.Reset();
        }

        // The scheduled work is done. From here on this is bookkeeping for it, not work a
        // cancellation can still usefully stop (effect/0005 applies the same rule to the outcome).
        RecordManifestSuccess(metadata);
        await dataContext.SaveChanges(CancellationToken.None);

        return Unit.Default;
    }

    private void RecordManifestSuccess(Metadata metadata)
    {
        if (metadata.Manifest is null)
        {
            logger.LogDebug(
                "No manifest associated with Metadata {MetadataId}, skipping LastSuccessfulRun update",
                metadata.Id
            );
            return;
        }

        metadata.Manifest.LastSuccessfulRun = DateTime.UtcNow;
        metadata.Manifest.NextScheduledRun = SchedulingHelpers.ComputeNextScheduledRun(
            metadata.Manifest
        );

        if (metadata.Manifest.ScheduleType == ScheduleType.Once)
        {
            metadata.Manifest.IsEnabled = false;
            logger.LogInformation(
                "Auto-disabled Once manifest {ManifestId} after successful execution",
                metadata.Manifest.Id
            );
        }

        logger.LogDebug(
            "Updated LastSuccessfulRun for Manifest {ManifestId} to {Timestamp}",
            metadata.Manifest.Id,
            metadata.Manifest.LastSuccessfulRun
        );
    }
}
