using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Workflows.GoodbyeWorld.Steps;

namespace Trax.Samples.Scheduler.Workflows.GoodbyeWorld;

/// <summary>
/// A "Goodbye World" workflow that demonstrates dependent scheduling.
/// Runs after HelloWorldWorkflow succeeds via .Then() chaining.
/// </summary>
public class GoodbyeWorldWorkflow : ServiceTrain<GoodbyeWorldInput, Unit>, IGoodbyeWorldWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(GoodbyeWorldInput input) =>
        Activate(input).Chain<LogFarewellStep>().Resolve();
}
