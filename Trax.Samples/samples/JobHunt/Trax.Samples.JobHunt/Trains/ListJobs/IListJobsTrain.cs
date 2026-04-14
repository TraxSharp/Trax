using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.ListJobs;

public interface IListJobsTrain : IServiceTrain<ListJobsInput, ListJobsOutput>;
