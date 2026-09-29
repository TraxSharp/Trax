using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckFactory;

/// <summary>
/// Creates the <see cref="ICancellationCheckProvider"/> for each train run by resolving it from the run's
/// service provider. Infrastructure registered by <c>AddJunctionProgress</c>; its type is the effect
/// registry key for the cancellation check. Not intended for direct use.
/// </summary>
/// <param name="serviceProvider">The provider the cancellation check is resolved from.</param>
public class CancellationCheckFactory(IServiceProvider serviceProvider)
    : IJunctionEffectProviderFactory
{
    /// <inheritdoc/>
    public IJunctionEffectProvider Create() =>
        serviceProvider.GetRequiredService<ICancellationCheckProvider>();
}
