using Trax.Effect.Services.StepEffectProvider;
using Trax.Effect.Services.StepEffectProviderFactory;
using Trax.Effect.StepProvider.Progress.Services.StepProgressProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StepProvider.Progress.Services.StepProgressFactory;

public class StepProgressFactory(IServiceProvider serviceProvider) : IStepEffectProviderFactory
{
    public IStepEffectProvider Create() =>
        serviceProvider.GetRequiredService<IStepProgressProvider>();
}
