using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.ListApplications;

public interface IListApplicationsTrain
    : IServiceTrain<ListApplicationsInput, ListApplicationsOutput>;
