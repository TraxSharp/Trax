using Trax.Effect.Configuration.TraxEffectConfiguration;
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
/// to use for parameter serialization. It keeps no reference to the providers it creates: each
/// belongs to the run that asked for it, which disposes it, so a provider and the parameters it
/// tracked are collectable once the run ends.
///
/// The factory is registered with the dependency injection container as an IEffectProviderFactory,
/// which allows the Trax.Effect system to create and use parameter effect providers without
/// directly depending on the concrete implementation.
/// </remarks>
/// <param name="configuration">The Trax.Core effect configuration containing JSON serialization options</param>
/// <param name="effectConfiguration">Runtime configuration controlling which parameters are serialized</param>
public class ParameterEffectProviderFactory(
    ITraxEffectConfiguration configuration,
    ParameterEffectConfiguration effectConfiguration
) : IParameterEffectProviderFactory
{
    /// <inheritdoc />
    public ParameterEffectConfiguration Configuration => effectConfiguration;

    /// <summary>
    /// Creates a new instance of a parameter effect provider.
    /// </summary>
    /// <returns>A new instance of IEffectProvider</returns>
    /// <remarks>
    /// This method creates a new instance of ParameterEffect, which is an implementation of
    /// IEffectProvider that serializes train input and output parameters to JSON format.
    ///
    /// The method performs the following operations:
    /// 1. Creates a new instance of ParameterEffect with the JSON serialization options from the configuration
    /// 2. Returns the new provider as an IEffectProvider, owned by the caller
    ///
    /// The created provider is returned as an IEffectProvider, which allows the Trax.Effect
    /// system to use it without directly depending on the concrete implementation.
    /// </remarks>
    public IEffectProvider Create() =>
        new ParameterEffect(configuration.SystemJsonSerializerOptions, effectConfiguration);
}
