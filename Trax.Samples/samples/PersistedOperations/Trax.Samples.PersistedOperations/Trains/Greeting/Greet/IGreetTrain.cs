using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.PersistedOperations.Trains.Greeting.Greet;

public interface IGreetTrain : IServiceTrain<GreetInput, GreetOutput>;
