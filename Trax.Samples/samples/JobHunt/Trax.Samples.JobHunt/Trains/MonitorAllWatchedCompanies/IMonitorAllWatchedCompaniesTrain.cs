using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.MonitorAllWatchedCompanies;

public interface IMonitorAllWatchedCompaniesTrain
    : IServiceTrain<MonitorAllWatchedCompaniesInput, MonitorAllWatchedCompaniesOutput>;
