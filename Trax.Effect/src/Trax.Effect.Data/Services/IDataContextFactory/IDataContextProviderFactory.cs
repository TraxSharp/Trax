using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Services.EffectProviderFactory;

namespace Trax.Effect.Data.Services.IDataContextFactory;

/// <summary>
/// Defines the contract for a factory that creates database contexts for the Trax.Effect.Data system.
/// This interface extends IEffectProviderFactory to integrate with the EffectRunner.
/// </summary>
/// <remarks>
/// The IDataContextProviderFactory interface is a key abstraction in the Trax.Effect.Data system.
/// It serves as a bridge between the Trax.Effect tracking system and database persistence,
/// allowing different database implementations to be used interchangeably.
///
/// This interface:
/// 1. Extends IEffectProviderFactory to integrate with the EffectRunner
/// 2. Adds an asynchronous method for creating database contexts
///
/// Different database implementations (PostgreSQL, InMemory, etc.) provide their own
/// implementations of this interface, allowing the system to work with various database systems
/// while maintaining a consistent interface.
///
/// The factory pattern used here provides several benefits:
/// 1. Separation of context creation from context usage
/// 2. Ability to configure contexts with dependencies and settings
/// 3. Support for different context types without changing consumer code
/// 4. Testability through factory mocking or substitution
///
/// Implementations of this interface are typically registered in the dependency injection
/// container using the AddEffect extension methods in ServiceExtensions.
/// </remarks>
public interface IDataContextProviderFactory : IEffectProviderFactory
{
    /// <summary>
    /// Creates a new database context asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests</param>
    /// <returns>A task that resolves to a new database context</returns>
    /// <remarks>
    /// This method is responsible for creating and configuring a new database context.
    /// The specific type of context created depends on the implementation of the factory.
    ///
    /// Implementations should ensure that the context is properly initialized with
    /// any required dependencies or configuration settings.
    ///
    /// This method is typically called when a new database context is needed for
    /// operations that require direct database access outside of the EffectRunner flow.
    /// </remarks>
    Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken);
}
