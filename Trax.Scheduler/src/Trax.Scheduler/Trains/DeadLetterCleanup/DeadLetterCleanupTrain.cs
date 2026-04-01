using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.DeadLetterCleanup.Junctions;

namespace Trax.Scheduler.Trains.DeadLetterCleanup;

/// <summary>
/// Deletes resolved dead letter entries older than the configured retention period.
/// </summary>
public class DeadLetterCleanupTrain
    : ServiceTrain<DeadLetterCleanupRequest, Unit>,
        IDeadLetterCleanupTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        DeadLetterCleanupRequest input
    ) => Activate(input).Chain<DeleteResolvedDeadLettersJunction>().Resolve();
}
