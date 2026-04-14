using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs.Junctions;

namespace Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs;

public class MonitorAllActiveJobsTrain
    : ServiceTrain<MonitorAllActiveJobsInput, MonitorAllActiveJobsOutput>,
        IMonitorAllActiveJobsTrain
{
    protected override MonitorAllActiveJobsOutput Junctions() => Chain<FanOutMonitorJunction>();
}
