using Trax.Effect.Services.StepEffectProvider;
using Trax.Effect.Services.StepEffectProviderFactory;
using Trax.Effect.StepProvider.Progress.Services.CancellationCheckProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StepProvider.Progress.Services.CancellationCheckFactory;

public class CancellationCheckFactory(IServiceProvider serviceProvider) : IStepEffectProviderFactory
{
    public IStepEffectProvider Create() =>
        serviceProvider.GetRequiredService<ICancellationCheckProvider>();
}
