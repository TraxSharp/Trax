using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Exceptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainBus;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Trains.ManifestManager.Utilities;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Trains.JobRunner.Junctions;

/// <summary>
/// Executes the train the job's metadata row names, by name through the TrainBus, with the resolved input, then records the
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

        // A row that is no longer Pending belongs to the delivery that started it, or was
        // cancelled before it started. This delivery records nothing more and completes, so its
        // transport acknowledges it rather than retrying.
        if (metadata.TrainState != TrainState.Pending)
        {
            LogOwnedElsewhere(metadata);
            return Unit.Default;
        }

        // Initialize the dormant dependent context so user train junctions
        // can activate dormant dependents of this parent manifest.
        // Uses AsyncLocal to flow across the DI scope boundary created by TrainBus.RunByNameAsync.
        if (metadata.ManifestId.HasValue)
            dormantDependentContext.Initialize(metadata.ManifestId.Value);

        try
        {
            logger.LogDebug(
                "Executing train {TrainName} for Metadata {MetadataId}",
                metadata.Name,
                metadata.Id
            );

            // By name: the train the row names runs, even when another train takes the same input type.
            await trainBus.RunByNameAsync(
                resolvedInput.TrainName,
                resolvedInput.Value,
                CancellationToken,
                metadata
            );

            logger.LogDebug(
                "Successfully executed train {TrainName} for Metadata {MetadataId}",
                metadata.Name,
                metadata.Id
            );
        }
        catch (TrainAlreadyStartedException started) when (started.MetadataId == metadata.Id)
        {
            // Another delivery claimed the row between the load and the start; the store decided
            // it, and the train's body has not run here. Same outcome as the check above.
            LogOwnedElsewhere(metadata);
            return Unit.Default;
        }
        finally
        {
            // Clear the AsyncLocal to prevent stale manifest IDs from leaking
            // into subsequent job executions on the same worker task.
            dormantDependentContext.Reset();
        }

        // The scheduled work is done. From here on this is bookkeeping for it, not work a
        // cancellation can still usefully stop (effect/0005 applies the same rule to the outcome).
        await RecordManifestSuccessAsync(metadata);
        await dataContext.SaveChanges(CancellationToken.None);

        return Unit.Default;
    }

    private void LogOwnedElsewhere(Metadata metadata) =>
        logger.LogInformation(
            "Metadata {MetadataId} ({TrainName}) is {TrainState}, not Pending; this delivery did "
                + "not run it",
            metadata.Id,
            metadata.Name,
            metadata.TrainState
        );

    private async Task RecordManifestSuccessAsync(Metadata metadata)
    {
        if (metadata.Manifest is null)
        {
            logger.LogDebug(
                "No manifest associated with Metadata {MetadataId}, skipping LastSuccessfulRun update",
                metadata.Id
            );
            return;
        }

        // By the database's clock: a dependent compares this with when its own run was
        // dispatched, which another machine stamped (see DatabaseClock).
        var manifestId = metadata.Manifest.Id;
        metadata.Manifest.LastSuccessfulRun = await DatabaseClock.UtcNowAsync(
            dataContext.Manifests.Where(m => m.Id == manifestId),
            CancellationToken.None
        );
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
