using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.FindContact.Junctions;

namespace Trax.Samples.JobHunt.Trains.FindContact;

[TraxAllowAnonymous]
[TraxMutation(Description = "Records a contact for a job posting")]
[TraxBroadcast]
public class FindContactTrain : ServiceTrain<FindContactInput, FindContactOutput>, IFindContactTrain
{
    protected override Task<Either<Exception, FindContactOutput>> Junctions() =>
        Chain<PersistContactJunction>().Resolve();
}
