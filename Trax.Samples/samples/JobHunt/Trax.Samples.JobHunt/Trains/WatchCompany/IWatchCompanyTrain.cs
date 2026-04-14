using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.WatchCompany;

public interface IWatchCompanyTrain : IServiceTrain<WatchCompanyInput, WatchCompanyOutput>;
