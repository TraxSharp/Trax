using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Trains.GoodbyeWorld.Steps;

namespace Trax.Samples.Scheduler.Trains.GoodbyeWorld;

/// <summary>
/// A "Goodbye World" train that demonstrates dependent scheduling.
/// Runs after HelloWorldTrain succeeds via .Then() chaining.
/// </summary>
public class GoodbyeWorldTrain : ServiceTrain<GoodbyeWorldInput, Unit>, IGoodbyeWorldTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(GoodbyeWorldInput input) =>
        Activate(input).Chain<LogFarewellStep>().Resolve();
}
