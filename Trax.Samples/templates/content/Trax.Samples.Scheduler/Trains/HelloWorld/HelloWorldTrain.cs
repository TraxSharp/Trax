using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Trains.HelloWorld.Junctions;

namespace Trax.Samples.Scheduler.Trains.HelloWorld;

/// <summary>
/// A simple scheduled train that logs a greeting on each interval.
/// </summary>
public class HelloWorldTrain : ServiceTrain<HelloWorldInput, Unit>, IHelloWorldTrain
{
    protected override Unit Junctions() => Chain<LogGreetingJunction>();
}
