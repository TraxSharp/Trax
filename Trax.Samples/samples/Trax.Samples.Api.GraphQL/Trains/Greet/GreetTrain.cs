using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Api.GraphQL.Trains.Greet.Steps;

namespace Trax.Samples.Api.GraphQL.Trains.Greet;

public class GreetTrain : ServiceTrain<GreetInput, Unit>, IGreetTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(GreetInput input) =>
        Activate(input).Chain<LogGreetingStep>().Resolve();
}
