using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressFactory;

/// <summary>
/// Creates the <see cref="IJunctionProgressProvider"/> for each train run with <see cref="ActivatorUtilities"/>, so
/// the container does not keep the disposable provider after the run has disposed it. Infrastructure registered by <c>AddJunctionProgress</c>; its type is the effect
/// registry key for junction progress. Not intended for direct use.
/// </summary>
/// <param name="serviceProvider">The provider junction progress is resolved from.</param>
internal class JunctionProgressFactory(IServiceProvider serviceProvider)
    : IJunctionEffectProviderFactory
{
    /// <inheritdoc/>
    public IJunctionEffectProvider Create() =>
        ActivatorUtilities.CreateInstance<Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider.JunctionProgressProvider>(
            serviceProvider
        );
}
