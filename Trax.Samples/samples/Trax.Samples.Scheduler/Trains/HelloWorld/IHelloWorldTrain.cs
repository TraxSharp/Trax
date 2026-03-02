using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Trains.HelloWorld;

/// <summary>
/// Interface for the HelloWorld train.
/// Used by the TrainBus for train resolution.
/// </summary>
public interface IHelloWorldTrain : IServiceTrain<HelloWorldInput, Unit>;
