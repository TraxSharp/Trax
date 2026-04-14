using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.ListApplications.Junctions;

namespace Trax.Samples.JobHunt.Trains.ListApplications;

[TraxQuery(Description = "Lists all applications for a user")]
public class ListApplicationsTrain
    : ServiceTrain<ListApplicationsInput, ListApplicationsOutput>,
        IListApplicationsTrain
{
    protected override ListApplicationsOutput Junctions() => Chain<LoadApplicationsJunction>();
}
