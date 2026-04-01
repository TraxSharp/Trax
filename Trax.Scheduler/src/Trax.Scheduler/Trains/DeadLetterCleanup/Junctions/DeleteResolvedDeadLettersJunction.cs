using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.DeadLetterCleanup.Junctions;

/// <summary>
/// Deletes resolved dead letter entries older than the configured retention period.
/// </summary>
/// <remarks>
/// Only dead letters in a terminal state (Retried or Acknowledged) with a ResolvedAt
/// timestamp older than <see cref="SchedulerConfiguration.DeadLetterRetentionPeriod"/>
/// are eligible for deletion. AwaitingIntervention dead letters are never deleted.
/// </remarks>
internal class DeleteResolvedDeadLettersJunction(
    IDataContext dataContext,
    SchedulerConfiguration configuration,
    ILogger<DeleteResolvedDeadLettersJunction> logger
) : EffectJunction<DeadLetterCleanupRequest, Unit>
{
    private const int BatchSize = 1000;

    public override async Task<Unit> Run(DeadLetterCleanupRequest input)
    {
        var cutoffTime = DateTime.UtcNow - configuration.DeadLetterRetentionPeriod;
        var totalDeleted = 0;

        logger.LogDebug("Deleting resolved dead letters older than {CutoffTime}", cutoffTime);

        while (true)
        {
            var batchIds = await dataContext
                .DeadLetters.Where(dl =>
                    dl.Status != DeadLetterStatus.AwaitingIntervention
                    && dl.ResolvedAt != null
                    && dl.ResolvedAt < cutoffTime
                )
                .Select(dl => dl.Id)
                .Take(BatchSize)
                .ToListAsync(CancellationToken);

            if (batchIds.Count == 0)
                break;

            // Delete work queue entries that reference these dead letters (FK safety)
            await dataContext
                .WorkQueues.Where(wq =>
                    wq.DeadLetterId.HasValue && batchIds.Contains(wq.DeadLetterId.Value)
                )
                .ExecuteDeleteAsync(CancellationToken);

            totalDeleted += await dataContext
                .DeadLetters.Where(dl => batchIds.Contains(dl.Id))
                .ExecuteDeleteAsync(CancellationToken);

            if (batchIds.Count < BatchSize)
                break;
        }

        if (totalDeleted > 0)
        {
            logger.LogInformation(
                "Dead letter cleanup completed: deleted {Count} resolved dead letters",
                totalDeleted
            );
        }
        else
        {
            logger.LogDebug("Dead letter cleanup completed: no expired entries found");
        }

        return Unit.Default;
    }
}
