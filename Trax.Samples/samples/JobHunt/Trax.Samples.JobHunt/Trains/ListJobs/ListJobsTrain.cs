using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.ListJobs.Junctions;

namespace Trax.Samples.JobHunt.Trains.ListJobs;

[TraxQuery(Description = "Lists jobs for a user, optionally filtered by status")]
public class ListJobsTrain : ServiceTrain<ListJobsInput, ListJobsOutput>, IListJobsTrain
{
    protected override Task<Either<Exception, ListJobsOutput>> Junctions() =>
        Chain<LoadJobsJunction>().Resolve();
}
