using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectStep;

namespace Trax.Scheduler.Workflows.TaskServerExecutor.Steps;

/// <summary>
/// Updates the Manifest's LastSuccessfulRun timestamp after successful workflow execution.
/// </summary>
internal class UpdateManifestSuccessStep(
    IDataContext dataContext,
    ILogger<UpdateManifestSuccessStep> logger
) : EffectStep<Metadata, Unit>
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

        logger.LogDebug(
            "Updated LastSuccessfulRun for Manifest {ManifestId} to {Timestamp}",
            input.Manifest.Id,
            input.Manifest.LastSuccessfulRun
        );

        return Unit.Default;
    }
}
