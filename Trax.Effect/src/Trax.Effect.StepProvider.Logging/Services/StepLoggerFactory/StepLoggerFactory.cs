using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.StepEffectProvider;
using Trax.Effect.Services.StepEffectProviderFactory;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerProvider;

namespace Trax.Effect.StepProvider.Logging.Services.StepLoggerFactory;

public class StepLoggerFactory(IServiceProvider serviceProvider) : IStepEffectProviderFactory
{
    public IStepEffectProvider Create() =>
        serviceProvider.GetRequiredService<IStepLoggerProvider>();
}
