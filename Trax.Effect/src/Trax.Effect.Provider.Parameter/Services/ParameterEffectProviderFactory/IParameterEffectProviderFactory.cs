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
/// Implementations of this interface are responsible for creating instances of
/// ParameterEffect, which serialize train input and output parameters to JSON format.
/// </remarks>
internal interface IParameterEffectProviderFactory
    : IConfigurableEffectProviderFactory<ParameterEffectConfiguration> { }
