using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.GetArtifacts.Junctions;

namespace Trax.Samples.JobHunt.Trains.GetArtifacts;

[TraxAllowAnonymous]
[TraxQuery(Description = "Lists generated artifacts (resume, cover letter) for a job")]
public class GetArtifactsTrain
    : ServiceTrain<GetArtifactsInput, GetArtifactsOutput>,
        IGetArtifactsTrain
{
    protected override Task<Either<Exception, GetArtifactsOutput>> Junctions() =>
        Chain<LoadArtifactsJunction>().Resolve();
}
