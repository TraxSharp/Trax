using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.MonitorJob.Junctions;

namespace Trax.Samples.JobHunt.Trains.MonitorJob;

public class MonitorJobTrain : ServiceTrain<MonitorJobInput, MonitorJobOutput>, IMonitorJobTrain
{
    protected override Task<Either<Exception, MonitorJobOutput>> Junctions() =>
        Chain<RefetchAndHashJunction>()
            .Chain<DiffAgainstLastSnapshotJunction>()
            .Chain<PersistSnapshotAndUpdateJobJunction>()
            .Resolve();
}
