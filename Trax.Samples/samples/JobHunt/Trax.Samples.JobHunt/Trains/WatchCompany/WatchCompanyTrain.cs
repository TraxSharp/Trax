using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.WatchCompany.Junctions;

namespace Trax.Samples.JobHunt.Trains.WatchCompany;

[TraxMutation(Description = "Starts watching a company's careers page")]
public class WatchCompanyTrain
    : ServiceTrain<WatchCompanyInput, WatchCompanyOutput>,
        IWatchCompanyTrain
{
    protected override WatchCompanyOutput Junctions() => Chain<PersistWatchedCompanyJunction>();
}
