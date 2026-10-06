using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.PersistedOperations.Trains.Users.LookupUser;

public interface ILookupUserTrain : IServiceTrain<LookupUserInput, UserProfile>;
