using System.Collections.Generic;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Services.EffectProviderFactory;

namespace Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;

/// <summary>
/// Defines a factory for creating parameter effect providers.
/// </summary>
/// <remarks>
/// The IParameterEffectProviderFactory interface extends the base IEffectProviderFactory
/// interface and adds functionality specific to parameter effect providers.
///
/// This interface provides access to a list of all parameter effect providers created
/// by the factory, which allows for tracking and managing the providers throughout
/// their lifecycle.
///
/// Implementations of this interface are responsible for creating instances of
/// ParameterEffect, which serialize workflow input and output parameters to JSON format.
/// </remarks>
public interface IParameterEffectProviderFactory
    : IConfigurableEffectProviderFactory<ParameterEffectConfiguration>
{
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
    public List<ParameterEffect> Providers { get; }
}
