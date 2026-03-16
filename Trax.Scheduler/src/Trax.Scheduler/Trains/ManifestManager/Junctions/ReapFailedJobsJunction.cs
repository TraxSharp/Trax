using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Reaps failed jobs by creating DeadLetter records for manifests that exceed their retry limit.
/// </summary>
/// <remarks>
/// This junction receives manifests from LoadManifestsJunction and identifies those that have
/// exceeded their max_retries count, moving them into the dead letter queue for manual intervention.
///
/// Dead letters are persisted immediately via SaveChanges() to ensure they survive
/// even if later junctions in the train fail.
///
/// The returned List&lt;DeadLetter&gt; is stored in the train's Memory and made available
/// to DetermineJobsToQueueJunction so it can exclude just-dead-lettered manifests.
/// </remarks>
internal class ReapFailedJobsJunction(
    IDataContext dataContext,
    ILogger<ReapFailedJobsJunction> logger
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

            if (view.FailedCount >= view.Manifest.MaxRetries)
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
                            $"Max retries exceeded: ({view.FailedCount}) failures >= ({view.Manifest.MaxRetries}) max retries",
                        RetryCount = view.FailedCount,
                    }
                );

                await dataContext.Track(deadLetter);
                deadLettersCreated.Add(deadLetter);
            }
        }

        // Persist all changes immediately to ensure dead letters survive train failure
        await dataContext.SaveChanges(CancellationToken);

        logger.LogInformation(
            "ReapFailedJobsJunction completed: {DeadLettersCreated} dead letters created",
            deadLettersCreated.Count
        );

        return deadLettersCreated;
    }
}
