using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.DeadLetterCleanup.Junctions;

namespace Trax.Scheduler.Trains.DeadLetterCleanup;

/// <summary>
/// Deletes resolved dead letter entries older than the configured retention period.
/// Infrastructure the scheduler registers and runs itself; not intended to be called directly.
/// </summary>
internal class DeadLetterCleanupTrain
    : ServiceTrain<DeadLetterCleanupRequest, Unit>,
        IDeadLetterCleanupTrain
{
    /// <summary>
    /// A single junction that deletes, in batches, dead letters already resolved (Acknowledged or
    /// Retried) whose <c>ResolvedAt</c> is older than
    /// <see cref="Configuration.SchedulerConfiguration.DeadLetterRetentionPeriod"/>. Dead letters
    /// awaiting intervention are never deleted, nor one whose requeued entry is still queued.
    /// </summary>
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<DeleteResolvedDeadLettersJunction>().Resolve();
}
