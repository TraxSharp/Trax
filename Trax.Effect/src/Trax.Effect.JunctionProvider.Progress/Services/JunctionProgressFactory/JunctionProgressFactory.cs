using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressFactory;

/// <summary>
/// Creates the <see cref="IJunctionProgressProvider"/> for each train run by resolving it from the run's
/// service provider. Infrastructure registered by <c>AddJunctionProgress</c>; its type is the effect
/// registry key for junction progress. Not intended for direct use.
/// </summary>
/// <param name="serviceProvider">The provider junction progress is resolved from.</param>
public class JunctionProgressFactory(IServiceProvider serviceProvider)
    : IJunctionEffectProviderFactory
{
    /// <inheritdoc/>
    public IJunctionEffectProvider Create() =>
        serviceProvider.GetRequiredService<IJunctionProgressProvider>();
}
