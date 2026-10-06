using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduling.Trains.SendDailyDigest.Junctions;

namespace Trax.Samples.Scheduling.Trains.SendDailyDigest;

/// <summary>Runs on a cron schedule, every day at 07:00 UTC.</summary>
public class SendDailyDigestTrain : ServiceTrain<SendDailyDigestInput, Unit>, ISendDailyDigestTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<SendDigestJunction>().Resolve();
}
