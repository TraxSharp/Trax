using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;

namespace Trax.Effect.Data.InMemory.Extensions;

/// <summary>
/// Provides extension methods for configuring Trax.Effect.Data.InMemory services in the dependency injection container.
/// </summary>
/// <remarks>
/// The ServiceExtensions class contains utility methods that simplify the registration
/// of Trax.Effect.Data.InMemory services with the dependency injection system.
///
/// These extensions enable:
/// 1. Easy configuration of in-memory database contexts
/// 2. Consistent service registration across different applications
/// 3. Integration with the Trax.Effect configuration system
///
/// By using these extensions, applications can easily configure and use the
/// Trax.Effect.Data.InMemory system with minimal boilerplate code.
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds in-memory database support to the Trax.Effect system.
    /// </summary>
    /// <param name="configurationBuilder">The Trax.Core effect configuration builder</param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <remarks>
    /// This method registers the InMemoryContextProviderFactory as an IDataContextProviderFactory
    /// with the dependency injection container, enabling the Trax.Effect system to use
    /// in-memory databases for workflow metadata persistence.
    ///
    /// The in-memory database implementation is particularly useful for:
    /// 1. Unit and integration testing
    /// 2. Development and debugging
    /// 3. Scenarios where persistence beyond the application lifecycle is not required
    ///
    /// Unlike production database implementations, this method requires no connection string
    /// or additional configuration, making it ideal for testing and development scenarios
    /// where database setup should be minimal.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddTraxEffects(options => options.AddInMemoryEffect());
    /// ```
    ///
    /// Note that data stored in the in-memory database is lost when the application stops,
    /// so this implementation is not suitable for production scenarios where data persistence
    /// is required.
    /// </remarks>
    public static TraxEffectConfigurationBuilder AddInMemoryEffect(
        this TraxEffectConfigurationBuilder configurationBuilder
    ) =>
        configurationBuilder.AddEffect<IDataContextProviderFactory, InMemoryContextProviderFactory>(
            toggleable: false
        );
}
