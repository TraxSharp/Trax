using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressFactory;

public class JunctionProgressFactory(IServiceProvider serviceProvider)
    : IJunctionEffectProviderFactory
{
    public IJunctionEffectProvider Create() =>
        serviceProvider.GetRequiredService<IJunctionProgressProvider>();
}
