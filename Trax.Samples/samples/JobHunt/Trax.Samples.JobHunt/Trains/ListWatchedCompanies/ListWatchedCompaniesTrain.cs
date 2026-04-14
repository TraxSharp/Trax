using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.ListWatchedCompanies.Junctions;

namespace Trax.Samples.JobHunt.Trains.ListWatchedCompanies;

[TraxQuery(Description = "Lists all watched companies for a user")]
public class ListWatchedCompaniesTrain
    : ServiceTrain<ListWatchedCompaniesInput, ListWatchedCompaniesOutput>,
        IListWatchedCompaniesTrain
{
    protected override ListWatchedCompaniesOutput Junctions() =>
        Chain<LoadWatchedCompaniesJunction>();
}
