using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckFactory;

/// <summary>
/// Creates the <see cref="ICancellationCheckProvider"/> for each train run with <see cref="ActivatorUtilities"/>, so
/// the container does not keep the disposable provider after the run has disposed it. Infrastructure registered by <c>AddJunctionProgress</c>; its type is the effect
/// registry key for the cancellation check. Not intended for direct use.
/// </summary>
/// <param name="serviceProvider">The provider the cancellation check is resolved from.</param>
internal class CancellationCheckFactory(IServiceProvider serviceProvider)
    : IJunctionEffectProviderFactory
{
    /// <inheritdoc/>
    public IJunctionEffectProvider Create() =>
        ActivatorUtilities.CreateInstance<Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider.CancellationCheckProvider>(
            serviceProvider
        );
}
