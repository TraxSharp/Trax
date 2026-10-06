using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduling.Trains.SendLaunchAnnouncement.Junctions;

namespace Trax.Samples.Scheduling.Trains.SendLaunchAnnouncement;

/// <summary>
/// Scheduled with <c>ScheduleOnce</c>: it runs once when its delay has passed, and its manifest
/// disables itself after the first success.
/// </summary>
public class SendLaunchAnnouncementTrain
    : ServiceTrain<SendLaunchAnnouncementInput, Unit>,
        ISendLaunchAnnouncementTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<AnnounceJunction>().Resolve();
}
