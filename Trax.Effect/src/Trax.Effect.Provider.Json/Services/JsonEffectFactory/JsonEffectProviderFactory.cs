using Trax.Effect.Provider.Json.Services.JsonEffect;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.Provider.Json.Services.JsonEffectFactory;

/// <summary>
/// Implements a factory for creating JSON effect providers.
/// </summary>
/// <remarks>
/// The JsonEffectProviderFactory class provides an implementation of the IEffectProviderFactory
/// interface that creates instances of JsonEffectProvider.
///
/// This factory uses the service provider to resolve and create instances of IJsonEffectProvider,
/// which allows for proper dependency injection and lifecycle management of the effect providers.
///
/// The factory is registered with the dependency injection container as an IEffectProviderFactory,
/// which allows the Trax.Effect system to create and use JSON effect providers without
/// directly depending on the concrete implementation.
/// </remarks>
/// <param name="serviceProvider">The service provider used to resolve dependencies</param>
public class JsonEffectProviderFactory(IServiceProvider serviceProvider) : IEffectProviderFactory
{
    /// <summary>
    /// Creates a new instance of a JSON effect provider.
    /// </summary>
    /// <returns>A new instance of IEffectProvider</returns>
    /// <remarks>
    /// This method uses the service provider to resolve and create an instance of IJsonEffectProvider.
    /// This allows for proper dependency injection and lifecycle management of the effect provider.
    ///
    /// The resolved provider is returned as an IEffectProvider, which allows the Trax.Effect
    /// system to use it without directly depending on the concrete implementation.
    ///
    /// Note: The commented-out code shows an alternative implementation that creates a new instance
    /// of JsonEffectProvider directly, which is not used in favor of the dependency injection approach.
    /// </remarks>
    public IEffectProvider Create() => serviceProvider.GetRequiredService<IJsonEffectProvider>();

    // Alternative implementation (commented out):
    // public IEffectProvider Create() => new JsonEffectProvider(loggerFactory.CreateLogger<JsonEffectProvider>());
}
