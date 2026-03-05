using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectStep;

namespace Trax.Scheduler.Trains.JobRunner.Steps;

/// <summary>
/// Loads the Metadata record from the database and uses the provided input.
/// </summary>
/// <remarks>
/// All callers now provide the train input via the work queue dispatch pipeline.
/// The Manifest is eagerly loaded so that UpdateManifestSuccessStep can persist
/// LastSuccessfulRun via SaveChanges.
/// </remarks>
internal class LoadMetadataStep(IDataContext dataContext, ILogger<LoadMetadataStep> logger)
    : EffectStep<RunJobRequest, (Metadata, ResolvedTrainInput)>
{
    public override async Task<(Metadata, ResolvedTrainInput)> Run(RunJobRequest input)
    {
        logger.LogDebug(
            "Loading metadata for job execution (MetadataId: {MetadataId})",
            input.MetadataId
        );

        // Always load with Manifest included (tracked so UpdateManifestSuccessStep works)
        var metadata = await dataContext
            .Metadatas.Include(x => x.Manifest)
            .FirstOrDefaultAsync(x => x.Id == input.MetadataId, CancellationToken);

        if (metadata is null)
            throw new TrainException($"Metadata with ID {input.MetadataId} not found");

        if (input.Input is null)
            throw new TrainException(
                $"Train input is required for Metadata ID {input.MetadataId}. All executions must provide input via the work queue dispatch pipeline."
            );

        logger.LogDebug(
            "Loaded metadata for train {TrainName} (MetadataId: {MetadataId})",
            metadata.Name,
            metadata.Id
        );

        return (metadata, new ResolvedTrainInput(input.Input));
    }
}
