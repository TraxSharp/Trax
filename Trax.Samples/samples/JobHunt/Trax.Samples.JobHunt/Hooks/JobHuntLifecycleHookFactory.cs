using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Samples.JobHunt.Hooks;

public class JobHuntLifecycleHookFactory(IServiceProvider serviceProvider)
    : ITrainLifecycleHookFactory
{
    public ITrainLifecycleHook Create() =>
        ActivatorUtilities.CreateInstance<JobHuntLifecycleHook>(serviceProvider);
}
