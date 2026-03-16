using Trax.Effect.Services.JunctionEffectProvider;

namespace Trax.Effect.Services.JunctionEffectProviderFactory;

public interface IJunctionEffectProviderFactory
{
    IJunctionEffectProvider Create();
}
