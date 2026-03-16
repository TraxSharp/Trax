using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerFactory;

public class JunctionLoggerFactory(IServiceProvider serviceProvider)
    : IJunctionEffectProviderFactory
{
    public IJunctionEffectProvider Create() =>
        serviceProvider.GetRequiredService<IJunctionLoggerProvider>();
}
