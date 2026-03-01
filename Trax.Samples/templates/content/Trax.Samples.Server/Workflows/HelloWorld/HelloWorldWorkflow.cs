using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Server.Workflows.HelloWorld.Steps;

namespace Trax.Samples.Server.Workflows.HelloWorld;

/// <summary>
/// A simple "Hello World" workflow that demonstrates scheduled execution.
/// This workflow takes a name as input and logs a greeting message.
/// </summary>
public class HelloWorldWorkflow : ServiceTrain<HelloWorldInput, Unit>, IHelloWorldWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(HelloWorldInput input) =>
        Activate(input).Chain<LogGreetingStep>().Resolve();
}
