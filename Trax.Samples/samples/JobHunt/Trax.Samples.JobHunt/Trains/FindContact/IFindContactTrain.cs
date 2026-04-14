using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.FindContact;

public interface IFindContactTrain : IServiceTrain<FindContactInput, FindContactOutput>;
