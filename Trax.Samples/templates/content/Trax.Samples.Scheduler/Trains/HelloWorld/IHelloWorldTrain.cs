using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Trains.HelloWorld;

public interface IHelloWorldTrain : IServiceTrain<HelloWorldInput, Unit>;
