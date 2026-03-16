using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Api.Trains.HelloWorld.Junctions;

namespace Trax.Samples.Api.Trains.HelloWorld;

/// <summary>
/// A simple mutation train that logs a greeting.
/// Exposed as a typed mutation field under mutation { dispatch { runHelloWorld(...) } }.
/// </summary>
[TraxMutation(GraphQLOperation.Run, Description = "Runs a hello world greeting")]
public class HelloWorldTrain : ServiceTrain<HelloWorldInput, Unit>, IHelloWorldTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(HelloWorldInput input) =>
        Activate(input).Chain<LogGreetingJunction>().Resolve();
}
