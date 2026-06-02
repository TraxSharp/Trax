using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials.Junctions;

namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;

[TraxAllowAnonymous]
[TraxMutation(Description = "Generates a tailored resume and cover letter for a job via Ollama")]
[TraxBroadcast]
public class GenerateApplicationMaterialsTrain
    : ServiceTrain<GenerateApplicationMaterialsInput, GenerateApplicationMaterialsOutput>,
        IGenerateApplicationMaterialsTrain
{
    protected override Task<Either<Exception, GenerateApplicationMaterialsOutput>> Junctions() =>
        Chain<LoadJobAndProfileJunction>()
            .Chain<GenerateResumeJunction>()
            .Chain<GenerateCoverLetterJunction>()
            .Chain<PersistArtifactsJunction>()
            .Resolve();
}
