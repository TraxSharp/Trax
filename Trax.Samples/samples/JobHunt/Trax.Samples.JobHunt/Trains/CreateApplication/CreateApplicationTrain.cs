using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.CreateApplication.Junctions;

namespace Trax.Samples.JobHunt.Trains.CreateApplication;

[TraxAllowAnonymous]
[TraxMutation(Description = "Creates a new application for a job posting")]
public class CreateApplicationTrain
    : ServiceTrain<CreateApplicationInput, CreateApplicationOutput>,
        ICreateApplicationTrain
{
    protected override Task<Either<Exception, CreateApplicationOutput>> Junctions() =>
        Chain<PersistApplicationJunction>().Resolve();
}
