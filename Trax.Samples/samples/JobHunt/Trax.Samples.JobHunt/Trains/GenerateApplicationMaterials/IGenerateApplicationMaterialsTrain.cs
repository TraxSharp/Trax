using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;

public interface IGenerateApplicationMaterialsTrain
    : IServiceTrain<GenerateApplicationMaterialsInput, GenerateApplicationMaterialsOutput>;
