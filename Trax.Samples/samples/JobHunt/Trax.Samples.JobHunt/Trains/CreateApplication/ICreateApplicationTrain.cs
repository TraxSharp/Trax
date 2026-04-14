using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.CreateApplication;

public interface ICreateApplicationTrain
    : IServiceTrain<CreateApplicationInput, CreateApplicationOutput>;
