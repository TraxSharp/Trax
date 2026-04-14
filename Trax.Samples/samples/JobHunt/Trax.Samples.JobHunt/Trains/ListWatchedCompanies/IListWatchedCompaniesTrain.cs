using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.ListWatchedCompanies;

public interface IListWatchedCompaniesTrain
    : IServiceTrain<ListWatchedCompaniesInput, ListWatchedCompaniesOutput>;
