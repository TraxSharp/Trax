using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.JobRunner.Junctions;

/// <summary>
/// Loads the Metadata record from the database and uses the provided input.
/// </summary>
/// <remarks>
/// All callers now provide the train input via the work queue dispatch pipeline.
/// The Manifest is eagerly loaded so that UpdateManifestSuccessJunction can persist
/// LastSuccessfulRun via SaveChanges.
/// </remarks>
internal class LoadMetadataJunction(
    IDataContext dataContext,
    ITrainRegistry trainRegistry,
    ITrainDiscoveryService trainDiscovery,
    ILogger<LoadMetadataJunction> logger
) : EffectJunction<RunJobRequest, (Metadata, ResolvedTrainInput)>
{
    public override async Task<(Metadata, ResolvedTrainInput)> Run(RunJobRequest input)
    {
        logger.LogDebug(
            "Loading metadata for job execution (MetadataId: {MetadataId})",
            input.MetadataId
        );

        // Always load with Manifest included (tracked so UpdateManifestSuccessJunction works)
        var metadata = await dataContext
            .Metadatas.Include(x => x.Manifest)
            .FirstOrDefaultAsync(x => x.Id == input.MetadataId, CancellationToken);

        if (metadata is null)
            throw new TrainException($"Metadata with ID {input.MetadataId} not found");

        if (input.Input is null)
            throw new TrainException(
                $"Train input is required for Metadata ID {input.MetadataId}. All executions must provide input via the work queue dispatch pipeline."
            );

        // The bus runs whichever train is registered for the input's type, so the row must belong
        // to that train. Checked before anything touches the row, which stays Pending.
        if (!trainRegistry.InputTypeToTrain.TryGetValue(input.Input.GetType(), out var trainType))
            throw new TrainException(
                $"No registered train takes the input given for Metadata ID {input.MetadataId}."
            );

        // The scheduler starts its own trains in its own process; a job never carries one.
        if (AdminTrains.Includes(trainType))
            throw new TrainException(
                $"Metadata ID {input.MetadataId} was given the input of '{trainType.FullName}', "
                    + "one of the scheduler's own trains, which a runner does not run."
            );

        if (!NamesTrain(metadata.Name, trainType))
            throw new TrainException(
                $"Metadata ID {input.MetadataId} belongs to train '{metadata.Name}', "
                    + $"not to '{trainType.FullName}', which takes the input given."
            );

        logger.LogDebug(
            "Loaded metadata for train {TrainName} (MetadataId: {MetadataId})",
            metadata.Name,
            metadata.Id
        );

        return (metadata, new ResolvedTrainInput(input.Input));
    }

    /// <summary>
    /// Whether a row's name is one of the names the train goes by: its interface's full or short
    /// name (the canonical name and the wire's fallback), or its class's, which older rows carry.
    /// </summary>
    private bool NamesTrain(string name, Type serviceType)
    {
        if (Matches(name, serviceType))
            return true;

        foreach (var registration in trainDiscovery.DiscoverTrains())
            if (
                registration.ServiceType == serviceType
                && Matches(name, registration.ImplementationType)
            )
                return true;

        return false;

        static bool Matches(string name, Type type) =>
            string.Equals(name, type.FullName, StringComparison.Ordinal)
            || string.Equals(name, type.Name, StringComparison.Ordinal);
    }
}
