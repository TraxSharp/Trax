using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Hub.Trains.HelloWorld.Junctions;

namespace Trax.Samples.Hub.Trains.HelloWorld;

/// <summary>
/// A mutation train that logs a greeting. Also scheduled to run every 20 seconds.
/// Exposed as a typed mutation field under mutation { dispatch { runHelloWorld(...) } }.
/// </summary>
[TraxMutation(GraphQLOperation.Run, Description = "Runs a hello world greeting")]
public class HelloWorldTrain : ServiceTrain<HelloWorldInput, Unit>, IHelloWorldTrain
{
    protected override Unit Junctions() => Chain<LogGreetingJunction>();
}
