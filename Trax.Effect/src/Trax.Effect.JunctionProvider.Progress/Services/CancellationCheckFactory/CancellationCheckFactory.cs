using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckFactory;

public class CancellationCheckFactory(IServiceProvider serviceProvider)
    : IJunctionEffectProviderFactory
{
    public IJunctionEffectProvider Create() =>
        serviceProvider.GetRequiredService<ICancellationCheckProvider>();
}
