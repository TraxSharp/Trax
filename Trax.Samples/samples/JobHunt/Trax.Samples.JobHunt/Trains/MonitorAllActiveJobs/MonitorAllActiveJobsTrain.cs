using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs.Junctions;

namespace Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs;

public class MonitorAllActiveJobsTrain
    : ServiceTrain<MonitorAllActiveJobsInput, MonitorAllActiveJobsOutput>,
        IMonitorAllActiveJobsTrain
{
    protected override Task<Either<Exception, MonitorAllActiveJobsOutput>> Junctions() =>
        Chain<FanOutMonitorJunction>().Resolve();
}
