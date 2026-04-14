using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.MonitorJob;

public interface IMonitorJobTrain : IServiceTrain<MonitorJobInput, MonitorJobOutput>;
