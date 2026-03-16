using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Services.JsonEffect;
using Trax.Effect.Provider.Json.Services.JsonEffectFactory;
using Trax.Effect.Services.EffectProviderFactory;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.Provider.Json.Extensions;

/// <summary>
/// Provides extension methods for configuring Trax.Effect.Json services in the dependency injection container.
/// </summary>
/// <remarks>
/// The ServiceExtensions class contains utility methods that simplify the registration
/// of Trax.Effect.Json services with the dependency injection system.
///
/// These extensions enable JSON serialization support for the Trax.Effect system,
/// allowing train models to be serialized to and from JSON format.
///
/// By using these extensions, applications can easily configure and use the
/// Trax.Effect.Json system with minimal boilerplate code.
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds JSON effect support to the Trax.Core effect configuration builder.
    /// </summary>
    /// <param name="configurationBuilder">The Trax.Core effect configuration builder</param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <remarks>
    /// This method configures the Trax.Effect system to use JSON serialization for
    /// tracking and logging model changes. It registers the necessary services with the
    /// dependency injection container.
    ///
    /// The method performs the following operations:
    /// 1. Registers the JsonEffectProvider as a transient service
    /// 2. Registers the JsonEffectProviderFactory as an IEffectProviderFactory
    ///
    /// This enables the Trax.Effect system to track model changes and serialize them
    /// to JSON format for logging or persistence.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddTrax(trax => trax
    ///     .AddEffects(effects => effects.AddJson())
    /// );
    /// ```
    /// </remarks>
    public static TBuilder AddJson<TBuilder>(this TBuilder configurationBuilder)
        where TBuilder : TraxEffectBuilder
    {
        configurationBuilder.ServiceCollection.AddTransient<
            IJsonEffectProvider,
            JsonEffectProvider
        >();

        configurationBuilder.AddEffect<JsonEffectProviderFactory>();
        return configurationBuilder;
    }
}
