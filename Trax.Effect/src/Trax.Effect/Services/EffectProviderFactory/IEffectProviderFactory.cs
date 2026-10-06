using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Services.EffectProviderFactory;

/// <summary>
/// Defines the contract for a factory that creates effect providers.
/// </summary>
/// <remarks>
/// The IEffectProviderFactory interface is part of the factory pattern used in the Trax.Effect system.
/// It abstracts the creation of effect providers, allowing for different types of providers
/// to be created and configured in a consistent way.
///
/// This pattern provides several benefits:
/// 1. Separation of provider creation from provider usage
/// 2. Ability to configure providers with dependencies and settings
/// 3. Support for different provider types without changing consumer code
/// 4. Testability through factory mocking or substitution
///
/// Implementations of this interface are typically registered in the dependency injection
/// container using the AddEffect extension methods in ServiceExtensions.
/// </remarks>
public interface IEffectProviderFactory
{
    /// <summary>
    /// Creates a new instance of an effect provider.
    /// </summary>
    /// <returns>A new effect provider instance</returns>
    /// <remarks>
    /// This method is responsible for creating and configuring a new effect provider.
    /// The specific type of provider created depends on the implementation of the factory.
    ///
    /// Implementations should ensure that the provider is properly initialized with
    /// any required dependencies or configuration settings.
    ///
    /// This method is typically called by the EffectRunner during initialization
    /// to create the providers that will be used for tracking and persistence.
    /// </remarks>
    IEffectProvider Create();
}
