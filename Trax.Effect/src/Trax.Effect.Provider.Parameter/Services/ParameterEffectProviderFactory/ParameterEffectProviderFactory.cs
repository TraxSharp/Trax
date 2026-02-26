using System.Collections.Generic;
using Trax.Effect.Configuration.Trax.CoreEffectConfiguration;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;

/// <summary>
/// Implements a factory for creating parameter effect providers.
/// </summary>
/// <remarks>
/// The ParameterEffectProviderFactory class provides an implementation of the IParameterEffectProviderFactory
/// interface that creates instances of ParameterEffect.
///
/// This factory uses the Trax.Core effect configuration to obtain the JSON serialization options
/// to use for parameter serialization. It also maintains a list of all parameter effect providers
/// created by the factory, which allows for tracking and managing the providers throughout
/// their lifecycle.
///
/// The factory is registered with the dependency injection container as an IEffectProviderFactory,
/// which allows the Trax.Effect system to create and use parameter effect providers without
/// directly depending on the concrete implementation.
/// </remarks>
/// <param name="configuration">The Trax.Core effect configuration containing JSON serialization options</param>
/// <param name="effectConfiguration">Runtime configuration controlling which parameters are serialized</param>
public class ParameterEffectProviderFactory(
    ITrax.CoreEffectConfiguration configuration,
    ParameterEffectConfiguration effectConfiguration
) : IParameterEffectProviderFactory
{
    /// <inheritdoc />
    public ParameterEffectConfiguration Configuration => effectConfiguration;

    /// <summary>
    /// Gets a list of all parameter effect providers created by this factory.
    /// </summary>
    /// <remarks>
    /// This property provides access to all parameter effect providers created by the factory.
    /// This allows for tracking and managing the providers throughout their lifecycle.
    ///
    /// The list is maintained by the factory and updated whenever a new provider is created.
    /// This enables the factory to keep track of all active providers and perform operations
    /// on them as needed, such as disposing them when they are no longer needed.
    /// </remarks>
    public List<ParameterEffect> Providers { get; } = [];

    /// <summary>
    /// Creates a new instance of a parameter effect provider.
    /// </summary>
    /// <returns>A new instance of IEffectProvider</returns>
    /// <remarks>
    /// This method creates a new instance of ParameterEffect, which is an implementation of
    /// IEffectProvider that serializes workflow input and output parameters to JSON format.
    ///
    /// The method performs the following steps:
    /// 1. Creates a new instance of ParameterEffect with the JSON serialization options from the configuration
    /// 2. Adds the new provider to the list of providers maintained by the factory
    /// 3. Returns the new provider as an IEffectProvider
    ///
    /// The created provider is returned as an IEffectProvider, which allows the Trax.Effect
    /// system to use it without directly depending on the concrete implementation.
    /// </remarks>
    public IEffectProvider Create()
    {
        var parameterEffect = new ParameterEffect(
            configuration.SystemJsonSerializerOptions,
            effectConfiguration
        );

        Providers.Add(parameterEffect);

        return parameterEffect;
    }
}
