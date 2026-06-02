using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.AddJob.Junctions;

namespace Trax.Samples.JobHunt.Trains.AddJob;

[TraxAllowAnonymous]
[TraxMutation(Description = "Adds a job posting from URL or pasted text")]
[TraxBroadcast]
public class AddJobTrain : ServiceTrain<AddJobInput, AddJobOutput>, IAddJobTrain
{
    protected override Task<Either<Exception, AddJobOutput>> Junctions() =>
        Chain<ValidateAddJobInputJunction>()
            .Chain<FetchJobPostingJunction>()
            .Chain<PersistJobJunction>()
            .Resolve();
}
