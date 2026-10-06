using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Reaps failed jobs by creating DeadLetter records for manifests that exceed their retry limit.
/// </summary>
/// <remarks>
/// This junction receives manifests from LoadManifestsJunction and identifies those that have
/// exceeded their max_retries count, moving them into the dead letter queue for manual intervention.
/// <c>MaxRetries</c> is the number of retries after the first run, so a manifest is dead-lettered
/// once its counted failures exceed it: <c>MaxRetries(0)</c> dead-letters on the first failure,
/// <c>MaxRetries(3)</c> on the fourth. A manifest with no counted failure is never dead-lettered.
///
/// Dead letters are saved here rather than at the end of the train, but the polling service
/// runs the whole ManifestManager cycle inside one transaction (the one holding the leader
/// lock), so they commit or roll back with the rest of the cycle. On InMemory there is no
/// transaction and the save is final.
///
/// The returned List&lt;DeadLetter&gt; is stored in the train's Memory and made available
/// to DetermineJobsToQueueJunction so it can exclude just-dead-lettered manifests.
/// </remarks>
internal class ReapFailedJobsJunction(
    IDataContext dataContext,
    ILogger<ReapFailedJobsJunction> logger,
    ITraxChangeSignal? changeSignal = null
) : EffectJunction<List<ManifestDispatchView>, List<DeadLetter>>
{
    public override async Task<List<DeadLetter>> Run(List<ManifestDispatchView> views)
    {
        logger.LogDebug("Starting ReapFailedJobsJunction to identify and dead-letter failed jobs");

        var deadLettersCreated = new List<DeadLetter>();

        logger.LogDebug(
            "Evaluating {ManifestCount} enabled manifests for dead-lettering",
            views.Count
        );

        foreach (var view in views)
        {
            if (view.HasAwaitingDeadLetter)
            {
                logger.LogTrace(
                    "Skipping manifest {ManifestId}: already has AwaitingIntervention dead letter",
                    view.Manifest.Id
                );
                continue;
            }

            // FailedCount > 0 first: a negative MaxRetries (a row written before the builder and
            // the operator surface refused one) must not dead-letter a manifest that never failed.
            if (view.FailedCount > 0 && view.FailedCount > view.Manifest.MaxRetries)
            {
                logger.LogWarning(
                    "Manifest {ManifestId} (name: {ManifestName}) exceeds max retries ({FailedCount}/{MaxRetries}). Creating dead letter.",
                    view.Manifest.Id,
                    view.Manifest.Name,
                    view.FailedCount,
                    view.Manifest.MaxRetries
                );

                var deadLetter = DeadLetter.Create(
                    new CreateDeadLetter
                    {
                        Manifest = view.Manifest,
                        Reason =
                            $"Max retries exceeded: ({view.FailedCount}) failures > ({view.Manifest.MaxRetries}) max retries",
                        RetryCount = view.FailedCount,
                    }
                );

                await dataContext.Track(deadLetter);
                deadLettersCreated.Add(deadLetter);
            }
        }

        // Saved within the cycle's transaction: a later junction's failure rolls these back too.
        await dataContext.SaveChanges(CancellationToken);

        if (deadLettersCreated.Count > 0)
            changeSignal?.Notify(ChangeDomain.DeadLetter);

        logger.LogInformation(
            "ReapFailedJobsJunction completed: {DeadLettersCreated} dead letters created",
            deadLettersCreated.Count
        );

        return deadLettersCreated;
    }
}
