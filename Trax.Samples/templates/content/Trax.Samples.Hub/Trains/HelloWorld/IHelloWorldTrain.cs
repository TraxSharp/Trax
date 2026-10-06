using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Hub.Trains.HelloWorld;

/// <summary>
/// How the rest of the application asks for the train: AddMediator registers HelloWorldTrain
/// under this interface, and its full name is the name Trax records each run under.
/// </summary>
public interface IHelloWorldTrain : IServiceTrain<HelloWorldInput, Unit>;
