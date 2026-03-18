using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Trains.JobRunner.Junctions;

/// <summary>
/// Updates the Manifest's LastSuccessfulRun timestamp after successful train execution.
/// </summary>
internal class UpdateManifestSuccessJunction(ILogger<UpdateManifestSuccessJunction> logger)
    : EffectJunction<Metadata, Unit>
{
    public override async Task<Unit> Run(Metadata input)
    {
        if (input.Manifest is null)
        {
            logger.LogDebug(
                "No manifest associated with Metadata {MetadataId}, skipping LastSuccessfulRun update",
                input.Id
            );
            return Unit.Default;
        }

        input.Manifest.LastSuccessfulRun = DateTime.UtcNow;
        input.Manifest.NextScheduledRun = SchedulingHelpers.ComputeNextScheduledRun(input.Manifest);

        if (input.Manifest.ScheduleType == ScheduleType.Once)
        {
            input.Manifest.IsEnabled = false;
            logger.LogInformation(
                "Auto-disabled Once manifest {ManifestId} after successful execution",
                input.Manifest.Id
            );
        }

        logger.LogDebug(
            "Updated LastSuccessfulRun for Manifest {ManifestId} to {Timestamp}",
            input.Manifest.Id,
            input.Manifest.LastSuccessfulRun
        );

        return Unit.Default;
    }
}
