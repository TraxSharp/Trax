using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerFactory;

/// <summary>
/// Creates the <see cref="IJunctionLoggerProvider"/> for each train run with <see cref="ActivatorUtilities"/>, so
/// the container does not keep the disposable provider after the run has disposed it. Infrastructure registered by <c>AddJunctionLogger</c>; its type is the key for
/// enabling or disabling the junction logger in the effect registry. Not intended for direct use.
/// </summary>
/// <param name="serviceProvider">The provider the junction logger is resolved from.</param>
internal class JunctionLoggerFactory(IServiceProvider serviceProvider)
    : IJunctionEffectProviderFactory
{
    /// <inheritdoc/>
    public IJunctionEffectProvider Create() =>
        ActivatorUtilities.CreateInstance<Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider>(
            serviceProvider
        );
}
