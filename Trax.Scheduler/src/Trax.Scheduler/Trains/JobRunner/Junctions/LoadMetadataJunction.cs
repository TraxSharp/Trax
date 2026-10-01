using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Trains.JobRunner.Junctions;

/// <summary>
/// Loads the Metadata record from the database and uses the provided input.
/// </summary>
/// <remarks>
/// A Pending run whose cancellation was requested before it started is recorded Cancelled here,
/// and the next junction then does not run it, whatever junction providers the host registers.
/// All callers now provide the train input via the work queue dispatch pipeline.
/// The Manifest is eagerly loaded so that UpdateManifestSuccessJunction can persist
/// LastSuccessfulRun via SaveChanges.
/// </remarks>
internal class LoadMetadataJunction(
    IDataContext dataContext,
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

        // The scheduler starts its own trains in its own process; a job never carries one.
        if (AdminTrains.FullNames.Contains(metadata.Name, StringComparer.Ordinal))
            throw SchedulerTrain(input.MetadataId, metadata.Name);

        // The row names its train, and that train is the one that runs: another train may take the
        // same input type. Checked before anything touches the row, which stays Pending.
        var registration =
            FindNamedTrain(metadata.Name, input.Input)
            ?? throw new TrainException(
                $"Metadata ID {input.MetadataId} belongs to train '{metadata.Name}', which is not "
                    + "a registered train taking the input given."
            );

        if (AdminTrains.Includes(registration))
            throw SchedulerTrain(input.MetadataId, registration.ServiceType.FullName!);

        if (metadata is { TrainState: TrainState.Pending, CancellationRequested: true })
            await RecordCancelledBeforeStartAsync(metadata);

        logger.LogDebug(
            "Loaded metadata for train {TrainName} (MetadataId: {MetadataId})",
            metadata.Name,
            metadata.Id
        );

        return (metadata, new ResolvedTrainInput(input.Input, registration.ServiceType.FullName!));
    }

    /// <summary>
    /// Records a Pending run whose cancellation was requested before it started as
    /// <c>Cancelled</c>, so the train is not run. The write matches only a row still Pending and
    /// still flagged, so a delivery that claimed the run meanwhile keeps it; the tracked row is
    /// then read back and the next junction sees its state either way. Bookkeeping for a run that
    /// will not start, so it is written on <see cref="CancellationToken.None"/>.
    /// </summary>
    /// <remarks>
    /// The flag is set by the batch, manifest and group cancels on Pending rows. Before this
    /// check only a host with a junction progress provider read it, at the run's first junction,
    /// so on any other host a cancelled Pending run still ran.
    /// </remarks>
    private async Task RecordCancelledBeforeStartAsync(Metadata metadata)
    {
        var endTime = DateTime.UtcNow;
        var flagged = dataContext.Metadatas.Where(m =>
            m.Id == metadata.Id && m.TrainState == TrainState.Pending && m.CancellationRequested
        );

        var cancelled = dataContext.SupportsSetUpdates()
            ? await flagged.ExecuteUpdateAsync(
                s =>
                    s.SetProperty(m => m.TrainState, TrainState.Cancelled)
                        .SetProperty(m => m.EndTime, endTime),
                CancellationToken.None
            )
            : await dataContext.UpdateEachAsync(
                flagged,
                m =>
                {
                    m.TrainState = TrainState.Cancelled;
                    m.EndTime = endTime;
                },
                CancellationToken.None
            );

        if (dataContext is DbContext db)
            await db.Entry(metadata).ReloadAsync(CancellationToken.None);

        if (cancelled > 0)
            logger.LogInformation(
                "Metadata {MetadataId} ({TrainName}) was cancelled before it started; recorded it "
                    + "cancelled and did not run it",
                metadata.Id,
                metadata.Name
            );
    }

    private static TrainException SchedulerTrain(long metadataId, string trainName) =>
        new(
            $"Metadata ID {metadataId} names '{trainName}', one of the scheduler's own trains, "
                + "which a runner does not run."
        );

    /// <summary>
    /// The registered train a row's name names, provided it takes <paramref name="input"/>. The
    /// canonical name is the interface's full name; the interface's short name (the wire's
    /// fallback) and the class's full or short name, which older rows carry, are accepted when
    /// exactly one train taking the input goes by them.
    /// </summary>
    private TrainRegistration? FindNamedTrain(string name, object input)
    {
        var trains = trainDiscovery.DiscoverTrains();

        var canonical = trains.FirstOrDefault(t =>
            string.Equals(t.ServiceType.FullName, name, StringComparison.Ordinal)
        );
        if (canonical is not null)
            return canonical.InputType.IsInstanceOfType(input) ? canonical : null;

        var named = trains
            .Where(t => Matches(name, t.ServiceType) || Matches(name, t.ImplementationType))
            .Where(t => t.InputType.IsInstanceOfType(input))
            .ToList();

        return named.Count == 1 ? named[0] : null;

        static bool Matches(string name, Type type) =>
            string.Equals(name, type.FullName, StringComparison.Ordinal)
            || string.Equals(name, type.Name, StringComparison.Ordinal);
    }
}
