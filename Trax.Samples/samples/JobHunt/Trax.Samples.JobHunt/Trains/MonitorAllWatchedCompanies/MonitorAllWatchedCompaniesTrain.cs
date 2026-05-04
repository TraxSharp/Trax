using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.MonitorAllWatchedCompanies.Junctions;

namespace Trax.Samples.JobHunt.Trains.MonitorAllWatchedCompanies;

public class MonitorAllWatchedCompaniesTrain
    : ServiceTrain<MonitorAllWatchedCompaniesInput, MonitorAllWatchedCompaniesOutput>,
        IMonitorAllWatchedCompaniesTrain
{
    protected override Task<Either<Exception, MonitorAllWatchedCompaniesOutput>> Junctions() =>
        Chain<FanOutCompanyMonitorJunction>().Resolve();
}
