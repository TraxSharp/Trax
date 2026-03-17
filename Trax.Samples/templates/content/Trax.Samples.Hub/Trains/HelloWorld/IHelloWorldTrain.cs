using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Hub.Trains.HelloWorld;

public interface IHelloWorldTrain : IServiceTrain<HelloWorldInput, Unit>;
