using Trax.Effect.Services.StepEffectProvider;

namespace Trax.Effect.Services.StepEffectProviderFactory;

public interface IStepEffectProviderFactory
{
    IStepEffectProvider Create();
}
