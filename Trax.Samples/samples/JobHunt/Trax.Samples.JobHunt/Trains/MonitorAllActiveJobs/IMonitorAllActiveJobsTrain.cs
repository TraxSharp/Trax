using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs;

public interface IMonitorAllActiveJobsTrain
    : IServiceTrain<MonitorAllActiveJobsInput, MonitorAllActiveJobsOutput>;
