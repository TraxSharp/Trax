using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Api.Trains.HelloWorld.Junctions;

namespace Trax.Samples.Api.Trains.HelloWorld;

/// <summary>
/// A simple mutation train that logs a greeting.
/// Exposed as a typed mutation field under mutation { dispatch { runHelloWorld(...) } }.
/// </summary>
[TraxAllowAnonymous]
[TraxMutation(GraphQLOperation.Run, Description = "Runs a hello world greeting")]
public class HelloWorldTrain : ServiceTrain<HelloWorldInput, Unit>, IHelloWorldTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<LogGreetingJunction>().Resolve();
}
