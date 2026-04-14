using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.FindContact.Junctions;

namespace Trax.Samples.JobHunt.Trains.FindContact;

[TraxMutation(Description = "Records a contact for a job posting")]
[TraxBroadcast]
public class FindContactTrain : ServiceTrain<FindContactInput, FindContactOutput>, IFindContactTrain
{
    protected override FindContactOutput Junctions() => Chain<PersistContactJunction>();
}
