using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Server.Trains.HelloWorld.Steps;

namespace Trax.Samples.Server.Trains.HelloWorld;

/// <summary>
/// A simple "Hello World" train that demonstrates scheduled execution.
/// This train takes a name as input and logs a greeting message.
/// </summary>
public class HelloWorldTrain : ServiceTrain<HelloWorldInput, Unit>, IHelloWorldTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(HelloWorldInput input) =>
        Activate(input).Chain<LogGreetingStep>().Resolve();
}
