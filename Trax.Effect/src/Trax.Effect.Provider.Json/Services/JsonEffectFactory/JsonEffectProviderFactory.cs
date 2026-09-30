using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Provider.Json.Services.JsonEffect;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;

namespace Trax.Effect.Provider.Json.Services.JsonEffectFactory;

/// <summary>
/// Creates the <see cref="JsonEffectProvider"/> for each train run.
/// </summary>
/// <remarks>
/// Each provider is built with <see cref="ActivatorUtilities"/> rather than resolved as a transient
/// from the container: the root container keeps every transient <see cref="IDisposable"/> it resolves
/// until the container itself is disposed, so resolving one per run held every run's provider, and
/// the models it tracked, for the life of the process. The run disposes the provider it is given.
/// </remarks>
/// <param name="serviceProvider">The provider the effect provider's dependencies are resolved from.</param>
internal class JsonEffectProviderFactory(IServiceProvider serviceProvider) : IEffectProviderFactory
{
    /// <summary>
    /// Creates a new JSON effect provider, owned by the caller.
    /// </summary>
    public IEffectProvider Create() =>
        ActivatorUtilities.CreateInstance<JsonEffectProvider>(serviceProvider);
}
